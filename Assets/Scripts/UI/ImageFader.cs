using UnityEngine;
using UnityEngine.UI;

public class ImageFader : MonoBehaviour
{
    [SerializeField]
    private Image[] imgs;

    [SerializeField]
    private Button btn;

    [SerializeField]
    private int curOn = 0;

    public void SetOn(int on)
    {
        curOn = on;
        if (btn) btn.targetGraphic = imgs[curOn];
    }

    private void Update()
    {
        for (int i = 0; i < imgs.Length; ++i)
            imgs[i].color = new(1f, 1f, 1f, Mathf.MoveTowards(imgs[i].color.a, i == curOn ? 1f : 0f, Time.deltaTime * 2f));
    }
}