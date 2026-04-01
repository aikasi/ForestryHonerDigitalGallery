using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[DefaultExecutionOrder(10)]
public class TouchEffect : MonoBehaviour
{
    [SerializeField]
    private RectTransform particlePrefab;

    [SerializeField]
    private RectTransform particleParent;

    [SerializeField]
    private int poolSize = 10;

    [SerializeField]
    private float lifetime = 1f;

    [SerializeField]
    private Image[] blockedAreas;

    public UnityEvent onAnyTouch;

    private RectTransform[] pool;
    private int poolIndex = 0;

    private float intendedScreenWidth = 1920f;

    private void Awake()
    {
        intendedScreenWidth = particleParent.rect.width;
        particlePrefab.gameObject.SetActive(false);
        pool = new RectTransform[poolSize];
        for (int i = 0; i < poolSize; i++)
        {
            var obj = Instantiate(particlePrefab, particleParent);
            obj.gameObject.SetActive(false);
            pool[i] = obj;
        }
    }

    private void Update()
    {
        if (Input.GetMouseButtonDown(0)) TrySpawn(Input.mousePosition);
        foreach (var touch in Input.touches)
        {
            if (touch.phase != TouchPhase.Began) continue;
            TrySpawn(touch.position);
        }
    }

    private void TrySpawn(Vector2 screenPos)
    {
        onAnyTouch?.Invoke();

        if (IsBlocked(screenPos)) return;

        var obj = pool[poolIndex];
        poolIndex = (poolIndex + 1) % poolSize;

        obj.anchoredPosition = screenPos * (intendedScreenWidth / Mathf.Max(1f, Screen.width));
        obj.gameObject.SetActive(false);
        obj.gameObject.SetActive(true);

        StartCoroutine(DeactivateAfter(obj.gameObject, lifetime));
    }

    private bool IsBlocked(Vector2 screenPos)
    {
        foreach (var area in blockedAreas)
        {
            if (!area.isActiveAndEnabled) continue;
            if (RectTransformUtility.RectangleContainsScreenPoint(area.rectTransform, screenPos, Camera.main))
                return true;
        }
        return false;
    }

    private System.Collections.IEnumerator DeactivateAfter(GameObject obj, float delay)
    {
        yield return new WaitForSeconds(delay);
        obj.SetActive(false);
    }
}