using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public class TouchEffect : MonoBehaviour
{
    [SerializeField]
    private GameObject prefabFX;

    [SerializeField]
    private int poolSize = 8;

    private RectTransform[] pools;

    public UnityEvent onAnyClick;
    private int curFX = 0;

    private void Start()
    {
        curFX = 0;
        prefabFX.SetActive(false);
        pools = new RectTransform[poolSize];
        for (int i = 0; i < poolSize; ++i)
        {
            var obj = Instantiate(prefabFX, transform);
            obj.name = $"{prefabFX.name} ({i})";
            obj.SetActive(false); // just in case
            pools[i] = obj.transform as RectTransform;
        }
    }

    private static IEnumerator HideLater(GameObject obj)
    {
        yield return new WaitForSeconds(1f);
        obj.SetActive(false);
    }

    private void Update()
    {
        bool isPressed = false;
        Vector2 inputPosition = Vector2.zero;

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            isPressed = true;
            inputPosition = Mouse.current.position.ReadValue();
        }
        else if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
        {
            isPressed = true;
            inputPosition = Touchscreen.current.primaryTouch.position.ReadValue();
        }

        if (isPressed)
        {
            pools[curFX].anchoredPosition = inputPosition;
            pools[curFX].gameObject.SetActive(true);

            StartCoroutine(HideLater(pools[curFX].gameObject));

            curFX = (curFX + 1) % poolSize;

            if (onAnyClick != null)
            {
                onAnyClick.Invoke();
            }
        }
    }
}