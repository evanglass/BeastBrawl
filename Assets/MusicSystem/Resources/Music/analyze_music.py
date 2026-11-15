#!/usr/bin/env python3

import csv
import json
import math
import re
import shutil
import subprocess
import sys
from datetime import datetime, timezone
from itertools import combinations
from pathlib import Path

import numpy as np


ROOT = Path(__file__).resolve().parent
OUTPUT = ROOT / "MusicAnalysis"

CACHE_PATH = OUTPUT / "analysis_cache.json"
CONFIG_PATH = OUTPUT / "lead_in_config.json"
SUMMARY_PATH = OUTPUT / "summary.json"

BPM = 100
BEATS_PER_BAR = 4
SAMPLE_RATE = 48000

FFT_SIZE = 16384
HOP_SIZE = 8192

VERSION = 5

BAR_SECONDS = 60.0 * BEATS_PER_BAR / BPM
MAIN_BARS = 4
MAIN_SECONDS = MAIN_BARS * BAR_SECONDS

PEAK_CEILING_DBFS = -1.0
MAX_RECOMMENDED_BOOST_DB = 6.0
MAX_RECOMMENDED_CUT_DB = -12.0

EXTENSIONS = {
    ".wav",
    ".mp3",
    ".aif",
    ".aiff",
    ".flac",
    ".m4a",
    ".ogg",
    ".aac"
}

BANDS = {
    "sub": (20, 60),
    "bass": (60, 250),
    "low_mid": (250, 500),
    "mid": (500, 2000),
    "upper_mid": (2000, 4000),
    "treble": (4000, 20000)
}


def rounded(value):
    if value is None:
        return None

    return round(float(value), 4)


def db(value):
    if value <= 0:
        return -120.0

    return 20.0 * math.log10(value)


def rms(samples):
    if samples.size == 0:
        return 0.0

    data = samples.astype(
        np.float64,
        copy=False
    )

    return math.sqrt(
        float(np.mean(data * data))
    )


def peak(samples):
    if samples.size == 0:
        return 0.0

    return float(
        np.max(np.abs(samples))
    )


def category_of(path):
    relative = path.relative_to(ROOT)

    for folder in reversed(relative.parts[:-1]):
        name = folder.lower()

        if "riff" in name:
            return "Riffs"

        if "lead" in name or "melod" in name:
            return "Leads"

        if "drum" in name:
            return "Drums"

        if "groove" in name:
            return "Grooves"

        if "master" in name or "solo" in name:
            return "Masters"

    name = path.stem.lower()

    if "riff" in name:
        return "Riffs"

    if "lead" in name:
        return "Leads"

    return "Other"


def find_files():
    return sorted(
        path
        for path in ROOT.rglob("*")
        if path.is_file()
        and path.suffix.lower() in EXTENSIONS
        and OUTPUT not in path.parents
        and ".music-analysis-venv" not in path.parts
    )


def decode_audio(path):
    command = [
        "ffmpeg",
        "-hide_banner",
        "-loglevel",
        "error",
        "-nostdin",
        "-i",
        str(path),
        "-map",
        "0:a:0",
        "-vn",
        "-ac",
        "2",
        "-ar",
        str(SAMPLE_RATE),
        "-f",
        "f32le",
        "pipe:1"
    ]

    result = subprocess.run(
        command,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE
    )

    if result.returncode != 0:
        raise RuntimeError(
            result.stderr.decode(
                "utf-8",
                errors="replace"
            ).strip()
        )

    data = np.frombuffer(
        result.stdout,
        dtype="<f4"
    )

    if data.size < 2:
        raise ValueError("Audio contains no samples")

    data = data[:data.size // 2 * 2]

    return data.reshape(-1, 2)


def segment(audio, start_bar, bar_count):
    first = int(round(
        start_bar * BAR_SECONDS * SAMPLE_RATE
    ))

    last = int(round(
        (start_bar + bar_count) *
        BAR_SECONDS * SAMPLE_RATE
    ))

    first = max(0, first)
    last = min(len(audio), last)

    return audio[first:last]


def explicit_intro_from_name(filename):
    stem = Path(filename).stem.lower()

    patterns = [
        r"(?:^|[_\-\s])(\d+)[_\-\s]*intro(?:s|bars?)?(?:$|[_\-\s])",
        r"(?:^|[_\-\s])intro[_\-\s]*(\d+)(?:$|[_\-\s])",
        r"(?:^|[_\-\s])(\d+)[_\-\s]*lead[_\-\s]*in(?:$|[_\-\s])",
        r"(?:^|[_\-\s])lead[_\-\s]*in[_\-\s]*(\d+)(?:$|[_\-\s])"
    ]

    for pattern in patterns:
        match = re.search(pattern, stem)

        if match:
            return int(match.group(1))

    return None


def filename_bar_count(filename):
    stem = Path(filename).stem.lower()

    match = re.search(
        r"(?:^|[_\-\s])(\d+)[_\-\s]*bars?(?:$|[_\-\s])",
        stem
    )

    if match:
        return int(match.group(1))

    return None


def load_overrides():
    if not CONFIG_PATH.exists():
        return {}

    try:
        data = json.loads(
            CONFIG_PATH.read_text()
        )
    except (OSError, ValueError):
        return {}

    overrides = {}

    for filename, entry in data.get(
        "overrides", {}
    ).items():
        overrides[filename] = entry

    # Compatibility with the previous analyzer.
    # Previous default zero entries are not treated
    # as explicit overrides.
    for filename, value in data.get(
        "lead_in_bars", {}
    ).items():
        if filename in overrides:
            continue

        if isinstance(value, (int, float)) and value > 0:
            overrides[filename] = {
                "lead_in_bars": int(value)
            }

    return overrides


def determine_bars(
    path,
    duration,
    overrides
):
    relative = str(path.relative_to(ROOT))
    category = category_of(path)

    measured_bars = int(round(
        duration / BAR_SECONDS
    ))

    filename_bars = filename_bar_count(
        path.name
    )

    explicit_intro = explicit_intro_from_name(
        path.name
    )

    intro = (
        explicit_intro
        if explicit_intro is not None
        else 0
    )

    source = (
        "Explicit filename"
        if explicit_intro is not None
        else "No intro specified"
    )

    override = overrides.get(relative)

    if isinstance(override, dict):
        if "lead_in_bars" in override:
            intro = int(
                override["lead_in_bars"]
            )
            source = "Manual override"

    elif isinstance(override, (int, float)):
        intro = int(override)
        source = "Manual override"

    intro = max(0, intro)

    if category in ("Riffs", "Leads"):
        outro = max(
            0,
            measured_bars - MAIN_BARS - intro
        )
    else:
        outro = 0
        intro = 0

    declared_bars = (
        filename_bars
        if filename_bars is not None
        else measured_bars
    )

    return {
        "lead_in_bars": intro,
        "outro_bars": outro,
        "measured_bars": measured_bars,
        "declared_bars": declared_bars,
        "intro_source": source,
        "filename_bars_match": (
            filename_bars is None
            or filename_bars == measured_bars
        )
    }


def fft_analysis(audio):
    frequencies = np.fft.rfftfreq(
        FFT_SIZE,
        d=1.0 / SAMPLE_RATE
    )

    window = np.hanning(
        FFT_SIZE
    ).astype(np.float64)

    window_energy = np.sum(
        window * window
    )

    accumulated = np.zeros(
        len(frequencies),
        dtype=np.float64
    )

    count = 0

    for start in range(
        0,
        max(1, len(audio) - FFT_SIZE + 1),
        HOP_SIZE
    ):
        frame = audio[
            start:start + FFT_SIZE
        ]

        if len(frame) < FFT_SIZE:
            frame = np.pad(
                frame,
                (
                    (0, FFT_SIZE - len(frame)),
                    (0, 0)
                )
            )

        frame = frame.astype(
            np.float64,
            copy=False
        )

        spectrum = np.fft.rfft(
            frame * window[:, None],
            axis=0
        )

        power = np.mean(
            np.abs(spectrum) ** 2,
            axis=1
        )

        power /= FFT_SIZE * window_energy

        power[1:-1] *= 2.0

        accumulated += power
        count += 1

    if count > 0:
        accumulated /= count

    output = {}

    for name, limits in BANDS.items():
        low, high = limits

        mask = (
            (frequencies >= low) &
            (frequencies < high)
        )

        band_power = float(
            np.sum(accumulated[mask])
        )

        output[name + "_dbfs"] = rounded(
            db(math.sqrt(max(0.0, band_power)))
        )

    total = float(
        np.sum(accumulated)
    )

    if total > 0:
        centroid = float(
            np.sum(
                frequencies * accumulated
            ) / total
        )
    else:
        centroid = 0.0

    output["spectral_centroid_hz"] = rounded(
        centroid
    )

    return output


def analyze_file(path, bars):
    audio = decode_audio(path)

    relative = str(
        path.relative_to(ROOT)
    )

    category = category_of(path)

    duration = len(audio) / SAMPLE_RATE

    intro = bars["lead_in_bars"]
    outro = bars["outro_bars"]

    first_bar = segment(
        audio,
        intro,
        1
    )

    main = segment(
        audio,
        intro,
        MAIN_BARS
    )

    expected_main_end = (
        intro + MAIN_BARS
    ) * BAR_SECONDS

    complete = (
        duration + 0.005 >=
        expected_main_end
    )

    full_rms = rms(audio)
    first_rms = rms(first_bar)
    main_rms = rms(main)

    full_peak = peak(audio)
    first_peak = peak(first_bar)
    main_peak = peak(main)

    result = {
        "file": relative,
        "category": category,
        "duration_seconds": rounded(duration),

        "lead_in_bars": intro,
        "main_bars": MAIN_BARS,
        "outro_bars": outro,

        "intro_source": bars["intro_source"],
        "measured_bars": bars["measured_bars"],
        "declared_bars": bars["declared_bars"],
        "filename_bars_match": bars[
            "filename_bars_match"
        ],

        "main_section_complete": complete,

        "full_rms_dbfs": rounded(
            db(full_rms)
        ),

        "first_main_bar_rms_dbfs": rounded(
            db(first_rms)
        ),

        "main_four_bar_rms_dbfs": rounded(
            db(main_rms)
        ),

        "peak_dbfs": rounded(
            db(full_peak)
        ),

        "first_main_bar_peak_dbfs": rounded(
            db(first_peak)
        ),

        "main_peak_dbfs": rounded(
            db(main_peak)
        ),

        "crest_factor_db": rounded(
            db(full_peak) - db(full_rms)
        )
    }

    result.update(
        fft_analysis(main)
    )

    return result


def mean(records, field):
    values = [
        item[field]
        for item in records
        if item.get(field) is not None
    ]

    if not values:
        return None

    return rounded(
        sum(values) / len(values)
    )


def aggregate(records):
    result = {
        "track_count": len(records)
    }

    fields = [
        "full_rms_dbfs",
        "first_main_bar_rms_dbfs",
        "main_four_bar_rms_dbfs",
        "peak_dbfs",
        "main_peak_dbfs",
        "crest_factor_db",
        "spectral_centroid_hz"
    ]

    fields.extend(
        name + "_dbfs"
        for name in BANDS
    )

    for field in fields:
        result["mean_" + field] = mean(
            records,
            field
        )

    return result


def add_gain_recommendations(
    records,
    combined
):
    entry_reference = combined.get(
        "mean_first_main_bar_rms_dbfs"
    )

    main_reference = combined.get(
        "mean_main_four_bar_rms_dbfs"
    )

    for track in records:
        track["entry_offset_db"] = None
        track["main_offset_db"] = None
        track["entry_target_gain_db"] = None
        track["main_target_gain_db"] = None
        track["peak_safe_entry_gain_db"] = None
        track["peak_safe_main_gain_db"] = None
        track["entry_result_peak_dbfs"] = None
        track["main_result_peak_dbfs"] = None
        track["recommendation"] = ""

        if track["category"] not in (
            "Riffs",
            "Leads"
        ):
            continue

        if not track["main_section_complete"]:
            track["recommendation"] = (
                "CHECK AUDIO: incomplete main section"
            )
            continue

        entry = track[
            "first_main_bar_rms_dbfs"
        ]

        main = track[
            "main_four_bar_rms_dbfs"
        ]

        peak_level = track["peak_dbfs"]

        entry_difference = (
            entry_reference - entry
        )

        main_difference = (
            main_reference - main
        )

        track["entry_offset_db"] = rounded(
            entry - entry_reference
        )

        track["main_offset_db"] = rounded(
            main - main_reference
        )

        track["entry_target_gain_db"] = rounded(
            entry_difference
        )

        track["main_target_gain_db"] = rounded(
            main_difference
        )

        headroom = (
            PEAK_CEILING_DBFS - peak_level
        )

        safe_entry = max(
            MAX_RECOMMENDED_CUT_DB,
            min(
                MAX_RECOMMENDED_BOOST_DB,
                entry_difference,
                headroom
            )
        )

        safe_main = max(
            MAX_RECOMMENDED_CUT_DB,
            min(
                MAX_RECOMMENDED_BOOST_DB,
                main_difference,
                headroom
            )
        )

        track["peak_safe_entry_gain_db"] = rounded(
            safe_entry
        )

        track["peak_safe_main_gain_db"] = rounded(
            safe_main
        )

        track["entry_result_peak_dbfs"] = rounded(
            peak_level + safe_entry
        )

        track["main_result_peak_dbfs"] = rounded(
            peak_level + safe_main
        )

        if abs(entry_difference) < 1.0:
            recommendation = (
                "Entry level close to group average"
            )

        elif entry_difference > 0:
            recommendation = (
                "Quieter entry: consider increasing gain"
            )

        else:
            recommendation = (
                "Louder entry: consider reducing gain"
            )

        if entry_difference > safe_entry + 0.1:
            recommendation += (
                "; boost limited by peak headroom "
                "or gain cap"
            )

        if abs(
            entry_difference - main_difference
        ) > 3.0:
            recommendation += (
                "; entry and main-section levels "
                "differ substantially"
            )

        if peak_level >= 0:
            recommendation += (
                "; possible sample clipping"
            )

        track["recommendation"] = recommendation


def pairwise_report(records):
    results = []

    for first, second in combinations(
        records,
        2
    ):
        entry_difference = (
            first["first_main_bar_rms_dbfs"] -
            second["first_main_bar_rms_dbfs"]
        )

        main_difference = (
            first["main_four_bar_rms_dbfs"] -
            second["main_four_bar_rms_dbfs"]
        )

        item = {
            "track_a": first["file"],
            "track_b": second["file"],
            "category_a": first["category"],
            "category_b": second["category"],

            "entry_difference_db": rounded(
                entry_difference
            ),

            "main_difference_db": rounded(
                main_difference
            ),

            "absolute_entry_difference_db": rounded(
                abs(entry_difference)
            ),

            "absolute_main_difference_db": rounded(
                abs(main_difference)
            )
        }

        for band in BANDS:
            field = band + "_dbfs"

            item[
                band + "_difference_db"
            ] = rounded(
                first[field] - second[field]
            )

        results.append(item)

    results.sort(
        key=lambda item: (
            -item["absolute_entry_difference_db"],
            item["track_a"],
            item["track_b"]
        )
    )

    return results


def save_csv(path, records):
    if not records:
        return

    fields = list(records[0].keys())

    with path.open(
        "w",
        newline="",
        encoding="utf-8"
    ) as handle:
        writer = csv.DictWriter(
            handle,
            fieldnames=fields,
            extrasaction="ignore"
        )

        writer.writeheader()

        for record in records:
            writer.writerow(record)


def format_db(value):
    if value is None:
        return "N/A"

    return f"{value:+.2f} dB"


def main():
    if shutil.which("ffmpeg") is None:
        sys.exit(
            "FFmpeg missing. Run: brew install ffmpeg"
        )

    OUTPUT.mkdir(
        parents=True,
        exist_ok=True
    )

    files = find_files()

    if not files:
        sys.exit("No audio files found")

    overrides = load_overrides()

    try:
        cache = json.loads(
            CACHE_PATH.read_text()
        )
    except (OSError, ValueError):
        cache = {}

    if cache.get("version") != VERSION:
        cache = {
            "version": VERSION,
            "files": {}
        }

    previous_files = cache.get("files", {})
    next_cache = {}

    records = []
    errors = []
    configurations = {}

    print()
    print("=" * 72)
    print("BEASTBRAWL AUDIO RMS + FFT ANALYSIS")
    print("=" * 72)
    print("BPM:", BPM)
    print("Main section:", MAIN_BARS, "bars")
    print("Files:", len(files))
    print()

    for number, path in enumerate(files, 1):
        relative = str(
            path.relative_to(ROOT)
        )

        try:
            stat = path.stat()

            # Filename and override metadata can be
            # determined without decoding the audio.
            # Decode during analysis if not cached.
            old = previous_files.get(relative)

            if old is not None:
                cached_duration = old.get(
                    "result", {}
                ).get("duration_seconds")
            else:
                cached_duration = None

            # Metadata probing is lightweight.
            probe = subprocess.run(
                [
                    "ffprobe",
                    "-v",
                    "error",
                    "-select_streams",
                    "a:0",
                    "-show_entries",
                    "format=duration",
                    "-of",
                    "default=noprint_wrappers=1:nokey=1",
                    str(path)
                ],
                capture_output=True,
                text=True
            )

            if probe.returncode == 0:
                duration = float(
                    probe.stdout.strip()
                )
            elif cached_duration is not None:
                duration = cached_duration
            else:
                audio = decode_audio(path)
                duration = len(audio) / SAMPLE_RATE

            bars = determine_bars(
                path,
                duration,
                overrides
            )

            configurations[relative] = bars

            signature = {
                "size": stat.st_size,
                "mtime_ns": stat.st_mtime_ns,
                "lead_in_bars": bars["lead_in_bars"],
                "outro_bars": bars["outro_bars"],
                "bpm": BPM,
                "sample_rate": SAMPLE_RATE,
                "version": VERSION
            }

            if (
                old is not None
                and old.get("signature") == signature
            ):
                record = dict(
                    old["result"]
                )
                state = "CACHED"
            else:
                record = analyze_file(
                    path,
                    bars
                )
                state = "ANALYZED"

            next_cache[relative] = {
                "signature": signature,
                "result": record
            }

            records.append(record)

            print(
                f"[{number}/{len(files)}] "
                f"{state}: {relative}"
            )

            if record["category"] in (
                "Riffs",
                "Leads"
            ):
                print(
                    "    Intro:",
                    record["lead_in_bars"],
                    "| Main:",
                    record["main_bars"],
                    "| Outro:",
                    record["outro_bars"]
                )

        except Exception as error:
            errors.append({
                "file": relative,
                "error": str(error)
            })

            print(
                f"[{number}/{len(files)}] "
                f"ERROR: {relative}: {error}"
            )

    if not records:
        sys.exit(
            "No tracks analyzed successfully"
        )

    # Only complete four-bar sections are used
    # to build the combined level reference.
    combined = [
        item
        for item in records
        if item["category"] in (
            "Riffs",
            "Leads"
        )
        and item["main_section_complete"]
    ]

    riffs = [
        item
        for item in combined
        if item["category"] == "Riffs"
    ]

    leads = [
        item
        for item in combined
        if item["category"] == "Leads"
    ]

    combined_average = aggregate(combined)

    if combined:
        add_gain_recommendations(
            records,
            combined_average
        )

    pairs = pairwise_report(combined)

    categories = {}

    for category in sorted({
        item["category"]
        for item in records
    }):
        subset = [
            item
            for item in records
            if item["category"] == category
        ]

        categories[category] = aggregate(subset)

    summary = {
        "generated_utc": datetime.now(
            timezone.utc
        ).isoformat(),

        "bpm": BPM,
        "bar_seconds": BAR_SECONDS,
        "main_bars": MAIN_BARS,

        "peak_ceiling_dbfs": PEAK_CEILING_DBFS,

        "combined_riffs_and_leads": combined_average,
        "riffs": aggregate(riffs),
        "leads": aggregate(leads),
        "categories": categories,

        "tracks": records,
        "pairwise_comparisons": pairs,
        "errors": errors
    }

    save_csv(
        OUTPUT / "all_tracks.csv",
        records
    )

    save_csv(
        OUTPUT / "riffs_and_leads.csv",
        sorted(
            combined,
            key=lambda item:
                item["first_main_bar_rms_dbfs"],
            reverse=True
        )
    )

    save_csv(
        OUTPUT / "pairwise_comparisons.csv",
        pairs
    )

    save_csv(
        OUTPUT / "gain_recommendations.csv",
        sorted(
            combined,
            key=lambda item:
                abs(
                    item["entry_offset_db"]
                    or 0.0
                ),
            reverse=True
        )
    )

    SUMMARY_PATH.write_text(
        json.dumps(
            summary,
            indent=2
        )
    )

    CACHE_PATH.write_text(
        json.dumps(
            {
                "version": VERSION,
                "files": next_cache
            },
            indent=2
        )
    )

    CONFIG_PATH.write_text(
        json.dumps(
            {
                "description": (
                    "Explicit filename intros are detected "
                    "automatically. Without an intro marker, "
                    "extra bars are treated as outros. "
                    "Use overrides only when the filename "
                    "does not describe the actual audio."
                ),
                "bpm": BPM,
                "overrides": overrides,
                "detected_tracks": configurations
            },
            indent=2
        )
    )

    print()
    print("=" * 72)
    print("COMBINED RIFF + LEAD REFERENCE")
    print("=" * 72)

    print("Riffs:", len(riffs))
    print("Leads:", len(leads))

    if combined:
        print(
            "First-bar average:",
            format_db(
                combined_average[
                    "mean_first_main_bar_rms_dbfs"
                ]
            )
        )

        print(
            "Four-bar average:",
            format_db(
                combined_average[
                    "mean_main_four_bar_rms_dbfs"
                ]
            )
        )

    print()
    print("=" * 72)
    print("INDIVIDUAL TRACK ANALYSIS")
    print("=" * 72)

    for track in sorted(
        combined,
        key=lambda item:
            item["first_main_bar_rms_dbfs"],
        reverse=True
    ):
        print()
        print(track["file"])

        print(
            "  Intro / Main / Outro:",
            track["lead_in_bars"],
            "/",
            track["main_bars"],
            "/",
            track["outro_bars"]
        )

        print(
            "  First main bar:",
            format_db(
                track["first_main_bar_rms_dbfs"]
            ),
            "dBFS RMS"
        )

        print(
            "  Four-bar main:",
            format_db(
                track["main_four_bar_rms_dbfs"]
            ),
            "dBFS RMS"
        )

        print(
            "  Full-file peak:",
            format_db(
                track["peak_dbfs"]
            ),
            "dBFS"
        )

        print(
            "  Entry offset:",
            format_db(
                track["entry_offset_db"]
            )
        )

        print(
            "  Main offset:",
            format_db(
                track["main_offset_db"]
            )
        )

        print(
            "  Raw entry gain target:",
            format_db(
                track["entry_target_gain_db"]
            )
        )

        print(
            "  Peak-safe entry adjustment:",
            format_db(
                track["peak_safe_entry_gain_db"]
            )
        )

        print(
            "  Recommendation:",
            track["recommendation"]
        )

    print()
    print("=" * 72)
    print("LARGEST TRACK LEVEL DIFFERENCES")
    print("=" * 72)

    for pair in pairs[:10]:
        print()
        print(pair["track_a"])
        print("  vs")
        print(pair["track_b"])

        print(
            "  Entry difference:",
            format_db(
                pair["absolute_entry_difference_db"]
            )
        )

        print(
            "  Main difference:",
            format_db(
                pair["absolute_main_difference_db"]
            )
        )

    print()
    print("=" * 72)
    print("SUMMARY AND RECOMMENDATIONS")
    print("=" * 72)

    flagged = [
        track
        for track in combined
        if abs(
            track["entry_offset_db"]
        ) >= 3.0
    ]

    print(
        "Tracks at least 3 dB from entry average:",
        len(flagged)
    )

    for track in flagged:
        print(
            " ",
            track["file"],
            "| offset:",
            format_db(
                track["entry_offset_db"]
            ),
            "| peak-safe adjustment:",
            format_db(
                track["peak_safe_entry_gain_db"]
            )
        )

    incomplete = [
        track
        for track in records
        if track["category"] in (
            "Riffs",
            "Leads"
        )
        and not track["main_section_complete"]
    ]

    if incomplete:
        print()
        print("INCOMPLETE MAIN SECTIONS:")

        for track in incomplete:
            print(" ", track["file"])

    mismatches = [
        track
        for track in records
        if not track["filename_bars_match"]
    ]

    if mismatches:
        print()
        print("FILENAME BAR COUNT MISMATCHES:")

        for track in mismatches:
            print(" ", track["file"])

    if errors:
        print()
        print("ANALYSIS ERRORS:")

        for error in errors:
            print(
                " ",
                error["file"],
                error["error"]
            )

    print()
    print("=" * 72)
    print("SAVED REPORTS")
    print("=" * 72)

    for filename in [
        "riffs_and_leads.csv",
        "gain_recommendations.csv",
        "pairwise_comparisons.csv",
        "all_tracks.csv",
        "summary.json",
        "analysis_cache.json",
        "lead_in_config.json"
    ]:
        print(
            OUTPUT / filename
        )

    print()
    print("No audio files were modified.")
    print("No gain corrections were applied.")
    print(
        "Recommendations are preliminary and "
        "require listening verification."
    )


if __name__ == "__main__":
    main()
