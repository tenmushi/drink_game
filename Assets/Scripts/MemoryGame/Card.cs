using System;
using UnityEngine;

public enum CardState
{
    Free,
    Selected,
    Returning,
    Matched
}

public class Card : MonoBehaviour
{
    [SerializeField] private MeshRenderer faceRenderer;
    [SerializeField] private float slotMoveSpeed = 8f;
    [SerializeField] private float returnMoveSpeed = 2f;

    public int PairId { get; private set; }
    public CardState State { get; private set; } = CardState.Free;
    public bool IsFaceUp { get; private set; }

    // The card's fixed slot on the shared ring. Position is derived live from
    // Time.time each frame, so a card that has been away (Selected/Returning)
    // always rejoins its slot in sync with the rest of the ring, never lagging behind.
    private Vector3 orbitCenter;
    private float orbitRadius;
    private float orbitAngularSpeed;
    private float slotAngleOffset;

    private Vector3 slotTarget;
    private Action onArrivedAtSlot;

    private Material backMaterial;
    private Material frontMaterial;

    void Awake()
    {
        if (faceRenderer == null)
        {
            faceRenderer = GetComponent<MeshRenderer>();
        }
    }

    public void Init(int pairId, Material back, Material front, Vector3 center, float radius, float angularSpeed, float initialAngle)
    {
        PairId = pairId;
        backMaterial = back;
        frontMaterial = front;
        orbitCenter = center;
        orbitRadius = radius;
        orbitAngularSpeed = angularSpeed;
        slotAngleOffset = initialAngle - angularSpeed * Time.time;
        transform.position = LiveOrbitPosition();
        State = CardState.Free;
        SetFaceUp(false);
    }

    Vector3 LiveOrbitPosition()
    {
        float angle = slotAngleOffset + orbitAngularSpeed * Time.time;
        float x = Mathf.Cos(angle) * orbitRadius;
        float y = Mathf.Sin(angle) * orbitRadius;
        return orbitCenter + new Vector3(x, y, 0f);
    }

    void Update()
    {
        switch (State)
        {
            case CardState.Free:
                transform.position = LiveOrbitPosition();
                break;
            case CardState.Selected:
                MoveTowardFixedTarget(slotMoveSpeed);
                break;
            case CardState.Returning:
                MoveTowardOrbitSlot();
                break;
        }
    }

    void MoveTowardFixedTarget(float speed)
    {
        transform.position = Vector3.MoveTowards(transform.position, slotTarget, speed * Time.deltaTime);
        if ((transform.position - slotTarget).sqrMagnitude < 0.0004f)
        {
            var callback = onArrivedAtSlot;
            onArrivedAtSlot = null;
            callback?.Invoke();
        }
    }

    void MoveTowardOrbitSlot()
    {
        Vector3 target = LiveOrbitPosition();
        transform.position = Vector3.MoveTowards(transform.position, target, returnMoveSpeed * Time.deltaTime);
        if ((transform.position - target).sqrMagnitude < 0.0009f)
        {
            State = CardState.Free;
        }
    }

    public void MoveToSlot(Vector3 target, Action onArrived)
    {
        State = CardState.Selected;
        slotTarget = target;
        onArrivedAtSlot = onArrived;
    }

    public void ReturnToFree()
    {
        SetFaceUp(false);
        State = CardState.Returning;
    }

    public void SetMatched()
    {
        State = CardState.Matched;
    }

    public void SetFaceUp(bool faceUp)
    {
        IsFaceUp = faceUp;
        if (faceRenderer != null)
        {
            faceRenderer.sharedMaterial = faceUp ? frontMaterial : backMaterial;
        }
    }
}
