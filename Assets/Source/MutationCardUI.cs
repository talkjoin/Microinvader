using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MutationCardUI : MonoBehaviour
{
    [Header("UI References")]
    public Image    Icon;
    public TMP_Text NameText;
    public TMP_Text DescriptionText;
    public TMP_Text CostText;
    public Button   ActionButton;
    public TMP_Text ActionButtonLabel;

    public void Init(MutationDefinition def, int cost, bool free, bool canAfford, Action onClick)
    {
        if (def == null) return;

        if (Icon != null) Icon.sprite = def.Icon;
        if (NameText != null) NameText.text = def.DisplayName;
        if (DescriptionText != null) DescriptionText.text = def.Description;
        if (CostText != null) CostText.text = free ? "FREE" : cost.ToString();

        if (ActionButtonLabel != null)
            ActionButtonLabel.text = free ? "Select" : (canAfford ? "Buy" : "Needs Points");

        if (ActionButton != null)
        {
            ActionButton.onClick.RemoveAllListeners();
            ActionButton.interactable = free || canAfford;
            ActionButton.onClick.AddListener(() => onClick?.Invoke());
        }
    }
}
