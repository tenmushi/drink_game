using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class MemoryGameManager : MonoBehaviour
{
    [Header("Board")]
    [SerializeField] private GameObject cardPrefab;
    [SerializeField] private int pairCount = 8;
    [SerializeField] private Vector3 ringCenter = new Vector3(0f, 1.5f, 2f);
    [SerializeField] private float ringRadius = 3.5f;
    [SerializeField] private float ringAngularSpeedDeg = 25f;

    [Header("Reveal Slots")]
    [SerializeField] private Transform revealSlotA;
    [SerializeField] private Transform revealSlotB;
    [SerializeField] private float revealDuration = 1f;

    [Header("Card Faces (プレースホルダー: 空なら自動生成、後で画像を割り当て可)")]
    [SerializeField] private List<Texture2D> cardFaceTextures = new List<Texture2D>();
    [SerializeField] private Texture2D cardBackTexture;

    private static readonly Color[] PlaceholderPalette =
    {
        Color.red, Color.green, Color.blue, Color.yellow,
        new Color(1f, 0.5f, 0f), Color.magenta, Color.cyan, new Color(0.6f, 0.3f, 0.9f),
        Color.white, new Color(0.3f, 0.7f, 0.3f), new Color(0.8f, 0.2f, 0.4f), new Color(0.2f, 0.5f, 0.8f)
    };

    private readonly List<Card> cards = new List<Card>();
    private readonly List<Card> selectedCards = new List<Card>();
    private Camera mainCamera;
    private bool inputLocked;
    private bool resolving;
    private int arrivedCount;

    void Awake()
    {
        mainCamera = Camera.main;
    }

    void Start()
    {
        BuildBoard();
    }

    void Update()
    {
        if (inputLocked || mainCamera == null) return;
        if (Mouse.current == null) return;
        if (!Mouse.current.leftButton.wasPressedThisFrame) return;

        Vector2 screenPos = Mouse.current.position.ReadValue();
        Ray ray = mainCamera.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out RaycastHit hit, 200f))
        {
            Card card = hit.collider.GetComponentInParent<Card>();
            if (card != null)
            {
                TrySelect(card);
            }
        }
    }

    void BuildBoard()
    {
        if (cardPrefab == null)
        {
            Debug.LogError("MemoryGameManager: cardPrefab is not assigned.");
            return;
        }

        Material backMaterial = CreatePlaceholderMaterial(cardBackTexture, Color.gray);
        var faceMaterials = new List<Material>(pairCount);
        for (int i = 0; i < pairCount; i++)
        {
            Texture2D tex = i < cardFaceTextures.Count ? cardFaceTextures[i] : null;
            Color color = PlaceholderPalette[i % PlaceholderPalette.Length];
            faceMaterials.Add(CreatePlaceholderMaterial(tex, color));
        }

        var pairIds = new List<int>(pairCount * 2);
        for (int i = 0; i < pairCount; i++)
        {
            pairIds.Add(i);
            pairIds.Add(i);
        }
        Shuffle(pairIds);

        int totalCards = pairIds.Count;
        float angularSpeed = ringAngularSpeedDeg * Mathf.Deg2Rad;
        for (int i = 0; i < totalCards; i++)
        {
            GameObject go = Instantiate(cardPrefab, transform);
            Card card = go.GetComponent<Card>();
            float startAngle = (Mathf.PI * 2f) * i / totalCards;
            card.Init(pairIds[i], backMaterial, faceMaterials[pairIds[i]], ringCenter, ringRadius, angularSpeed, startAngle);
            cards.Add(card);
        }
    }

    Material CreatePlaceholderMaterial(Texture2D tex, Color fallback)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }
        var mat = new Material(shader);
        if (tex != null)
        {
            mat.mainTexture = tex;
            mat.color = Color.white;
        }
        else
        {
            mat.color = fallback;
        }
        return mat;
    }

    void TrySelect(Card card)
    {
        if (card.State != CardState.Free) return;
        if (selectedCards.Count >= 2) return;
        if (selectedCards.Contains(card)) return;

        Transform slot = selectedCards.Count == 0 ? revealSlotA : revealSlotB;
        if (slot == null) return;

        selectedCards.Add(card);
        card.MoveToSlot(slot.position, () => OnCardArrivedAtSlot(card));
    }

    void OnCardArrivedAtSlot(Card card)
    {
        card.SetFaceUp(true);
        arrivedCount++;
        if (!resolving && selectedCards.Count == 2 && arrivedCount >= 2)
        {
            resolving = true;
            inputLocked = true;
            Invoke(nameof(ResolveSelection), revealDuration);
        }
    }

    void ResolveSelection()
    {
        resolving = false;
        arrivedCount = 0;
        Card a = selectedCards[0];
        Card b = selectedCards[1];
        selectedCards.Clear();
        inputLocked = false;

        if (a.PairId == b.PairId)
        {
            a.SetMatched();
            b.SetMatched();
            a.gameObject.SetActive(false);
            b.gameObject.SetActive(false);
            cards.Remove(a);
            cards.Remove(b);
        }
        else
        {
            a.ReturnToFree();
            b.ReturnToFree();
        }
    }

    static void Shuffle(List<int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
