using UnityEngine;

public class ReactivePanel : MonoBehaviour
{
    [SerializeField]
    private PlaybackManager mgr;

    [SerializeField]
    private ImageFader[] fadeMaps;

    [SerializeField]
    private ImageFader[] fadeArrows;

    [SerializeField]
    private ImageFader fadeBottomText;

    private int CurVid => mgr.CurrentPlayingIndex;

    private int lastVid = -2;

    private void Update()
    {
        if (lastVid != CurVid) UpdateUIs();
    }

    private void UpdateUIs()
    {
        lastVid = CurVid;
        Debug.Log(lastVid);
        bool isOff = lastVid <= 0;
        for (int i = 0; i < fadeMaps.Length; ++i)
        {
            fadeMaps[i].SetOn(isOff ? 1 : (i == lastVid - 1 ? 1 : 0));
            fadeArrows[i].SetOn(isOff ? 2 : (i == lastVid - 1 ? 1 : 0));
        }
        fadeBottomText.SetOn(isOff ? 0 : 1);
    }
}