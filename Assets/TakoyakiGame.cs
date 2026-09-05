using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class TakoyakiGame : MonoBehaviour
{
    public Button[] takoyakiButtons;
    public TMP_Text resultText;

    private int hazureNumber;

    void Start()
    {
        // 0～5の中からハズレを1つランダムに決める
        hazureNumber = Random.Range(0, 6);

        resultText.text = "Choose one takoyaki";

        // 6個のボタンにクリック処理を設定
        for (int i = 0; i < takoyakiButtons.Length; i++)
        {
            int number = i;

            takoyakiButtons[i].onClick.AddListener(() => ChooseTakoyaki(number));
        }
    }

    void ChooseTakoyaki(int number)
    {
        if (number == hazureNumber)
        {
            resultText.text = "Miss!";
        }
        else
        {
            resultText.text = "Safe!";
        }
    }
}