#if !UNITY_SERVER || UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;

/// <summary>Applies the supplied card artwork after the lobby updates its labels.</summary>
public sealed class LobbyCardPresentation : MonoBehaviour
{
    private Image[] cards;
    private Text[] characterNames;
    private Sprite emptyTile;
    private Sprite occupiedTile;

    private void Awake()
    {
        Texture2D texture = Resources.Load<Texture2D>("MenuArt/CharacterTiles");
        if (texture == null || texture.width < 374 || texture.height < 228)
        {
            enabled = false;
            return;
        }
        texture.filterMode = FilterMode.Point;
        emptyTile = Sprite.Create(texture, new Rect(0, 0, 187, 228), new Vector2(.5f, .5f));
        occupiedTile = Sprite.Create(texture, new Rect(187, 0, 187, 228), new Vector2(.5f, .5f));
        cards = new Image[4];
        characterNames = new Text[4];
        foreach (Transform item in GetComponentsInChildren<Transform>(true))
        {
            for (int i = 0; i < cards.Length; i++)
            {
                if (item.name != "Player Card " + (i + 1)) continue;
                cards[i] = item.GetComponent<Image>();
                Transform label = item.Find("Character Name");
                if (label != null) characterNames[i] = label.GetComponent<Text>();
            }
        }
    }

    private void LateUpdate()
    {
        for (int i = 0; i < cards.Length; i++)
        {
            if (cards[i] == null || characterNames[i] == null) continue;
            cards[i].sprite = string.IsNullOrEmpty(characterNames[i].text) ? emptyTile : occupiedTile;
            cards[i].color = Color.white;
        }
    }
}
#endif
