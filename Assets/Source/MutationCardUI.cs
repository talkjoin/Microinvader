// MutationCardUI.cs
// One mutation option card. GameManager instantiates one of these per
// choice on both the free-mutation-pick screen (after Continue) and the
// portal shop screen. Build a prefab with these fields wired up: an
// icon Image, name/description TMP_Text fields, a cost TMP_Text, and a
// Button with its own label text.

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
