using ProjectM.UI;
using ProjectM.Network;
using ProjectM;
using ScarletTeleportGUI.Utilities;
using TMPro;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ScarletTeleportGUI.Services;

internal class CanvasService
{
    enum TeleportCategory
    {
        Private,
        Public,
        Player,
        Waypoints
    }

    const float PANEL_W = 340f;
    const float PANEL_H = 290f;
    const float HEADER_H = 34f;
    const float TAB_H = 34f;
    const float NAV_H = 56f;
    const float CARD_H = 28f;
    const float CARD_GAP = 3f;
    const int MAX_CARDS = 5;
    const float PLAYER_LIST_W = 250f;
    const float PLAYER_LIST_H = 290f;
    const int PLAYER_LIST_ROWS = 10;
    static readonly Color PanelBg = new(0.05f, 0.04f, 0.07f, 0.92f);
    static readonly Color HeaderBg = new(0.08f, 0.07f, 0.10f, 0.85f);
    static readonly Color TabsBg = new(0.07f, 0.06f, 0.09f, 0.85f);
    internal static readonly Color CardBg = new(0.08f, 0.07f, 0.10f, 0.78f);
    static readonly Color PlayerBg = new(0.08f, 0.07f, 0.10f, 0.70f);
    static readonly Color InputBg = new(0.10f, 0.09f, 0.13f, 0.92f);
    static readonly Color ButtonBg = new(0.25f, 0.18f, 0.08f, 0.90f);
    static readonly Color ButtonBgAlt = new(0.25f, 0.18f, 0.08f, 0.90f);
    static readonly Color SeparatorBg = new(0.50f, 0.40f, 0.20f, 0.70f);
    static readonly Color TextPrimary = new(0.95f, 0.92f, 0.86f, 1f);
    internal static readonly Color TextSecondary = new(0.88f, 0.84f, 0.76f, 0.95f);
    internal static readonly Color TextMuted = new(0.78f, 0.72f, 0.66f, 0.55f);

    static readonly WaitForSeconds _fastWait = new(0.1f);
    static readonly WaitForSeconds _slowWait = new(1.0f);
    static readonly WaitForSeconds _actionDelay = new(0.35f);
    static WaitForSeconds WaitForSeconds => Plugin.Eclipsed ? _fastWait : _slowWait;

    static bool _ready;
    static bool _panelOpen;
    static bool _killSwitch;
    static bool _holdPlayerSuppression;
    static float _suppressionGraceUntil;
    static float _blockMapInputUntil;
    static float _forceSuppressUntil;
    static float _nextOnlinePlayerCacheRefresh;
    static float _nextSocialMenuPrimeAttempt;
    static bool _socialMenuPrimeInProgress;
    static TeleportCategory _activeCategory;
    static int _page;

    internal static bool IsOpen => _panelOpen;
    internal static bool BlockMapInput => Time.realtimeSinceStartup < _blockMapInputUntil;
    internal static bool SuppressGameplayInput
    {
        get
        {
            if (Time.realtimeSinceStartup < _forceSuppressUntil) return true;
            if (_holdPlayerSuppression) return true;
            if (Time.realtimeSinceStartup < _suppressionGraceUntil) return true;
            if (!_panelOpen || _activeCategory != TeleportCategory.Player) return false;
            return _playerNameField != null && _playerNameField.isFocused;
        }
    }

    static Canvas? _bottomBarCanvas;
    static int _layer;

    static GameObject? _iconButton;
    static GameObject? _panel;
    static GameObject? _cardsRoot;
    static GameObject? _playerForm;
    static GameObject? _playerListPanel;
    static GameObject? _navBar;
    static RectTransform? _playerInputRoot;
    static TextMeshProUGUI? _titleLabel;
    static TextMeshProUGUI? _pageLabel;
    static TextMeshProUGUI? _playerListPageLabel;
    static TMP_InputField? _playerNameField;
    static TextMeshProUGUI? _playerHintLabel;
    static readonly List<TeleportCard> _cards = new();
    static readonly List<TextMeshProUGUI> _playerListRows = new();
    static readonly List<string> _discoveredOnlinePlayers = new();
    static bool _playerListOpen;
    static int _playerListPage;
    static bool _loggedPlayerSourceDiagnostics;

    internal CanvasService(UICanvasBase canvas)
    {
        _killSwitch = false;
        _ready = false;
        _panelOpen = false;
        _activeCategory = TeleportCategory.Private;
        _page = 0;
        _holdPlayerSuppression = false;
        _suppressionGraceUntil = 0f;
        _blockMapInputUntil = 0f;
        _forceSuppressUntil = 0f;
        _nextOnlinePlayerCacheRefresh = 0f;
        _nextSocialMenuPrimeAttempt = 0f;
        _socialMenuPrimeInProgress = false;
        _playerListPage = 0;
        _cards.Clear();

        _bottomBarCanvas = canvas.BottomBarParent.gameObject.GetComponent<Canvas>();
        _layer = _bottomBarCanvas.gameObject.layer;

        if (!Plugin.TeleportsPanel) return;

        BuildIconButton();
        BuildPanel();

        _panel?.SetActive(false);

        if (_iconButton != null)
        {
            _ready = true;
            Core.StartCoroutine(UpdateLoop());
        }
    }

    static System.Collections.IEnumerator UpdateLoop()
    {
        while (true)
        {
            if (_killSwitch) yield break;

            if (_ready)
            {
                TeleportDataService.FinalizeIfExpired();

                if (Time.realtimeSinceStartup >= _nextOnlinePlayerCacheRefresh)
                {
                    RefreshOnlinePlayerCache();
                    _nextOnlinePlayerCacheRefresh = Time.realtimeSinceStartup + 5f;

                    if (_panelOpen && _activeCategory == TeleportCategory.Player && _discoveredOnlinePlayers.Count == 0)
                        TryPrimeSocialMenuRoster();
                }

                if (ShouldReleasePlayerInput())
                    DeactivatePlayerInput();

                if (_panelOpen && TeleportDataService.IsDirty)
                {
                    RefreshPage();
                    TeleportDataService.IsDirty = false;
                }
            }

            yield return _panelOpen && _activeCategory == TeleportCategory.Player ? null : WaitForSeconds;
        }
    }

    static void BuildIconButton()
    {
        _iconButton = new GameObject("STP_Icon");
        AttachToCanvas(_iconButton);

        var rt = _iconButton.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(104f, 60f);
        rt.sizeDelta = new Vector2(76f, 28f);

        var bg = _iconButton.AddComponent<Image>();
        bg.color = new Color(0.06f, 0.06f, 0.12f, 0.85f);

        var labelGO = MakeChild("IconLabel", _iconButton.transform);
        var label = labelGO.AddComponent<TextMeshProUGUI>();
        label.text = "STP";
        label.fontSize = 12f;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        FillParent(labelGO);

        _iconButton.AddComponent<Button>().onClick.AddListener((UnityAction)OnIconClicked);
        _iconButton.SetActive(true);
    }

    static void BuildPanel()
    {
        _panel = new GameObject("ScarletTeleportGUI_Panel");
        _panel.SetActive(false);
        AttachToCanvas(_panel);

        var rt = _panel.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(PANEL_W, PANEL_H);

        _panel.AddComponent<Image>().color = PanelBg;

        BuildHeader();
        BuildTabs();
        BuildCards();
        BuildPlayerForm();
        BuildPlayerListPanel();
        BuildNav();

        _panel.SetActive(false);
    }

    static void BuildHeader()
    {
        var header = MakeChild("Header", _panel!.transform);
        header.AddComponent<Image>().color = HeaderBg;
        Stretch(header, 0, 0, PANEL_H - HEADER_H, 0);

        var titleGO = MakeChild("Title", header.transform);
        _titleLabel = titleGO.AddComponent<TextMeshProUGUI>();
        _titleLabel.text = "Scarlet Teleports";
        _titleLabel.fontSize = 12f;
        _titleLabel.alignment = TextAlignmentOptions.Left;
        _titleLabel.enableWordWrapping = false;
        Frac(titleGO, 0.04f, 0.1f, 0.58f, 0.9f, 4, 0, 2, 2);

        var closeButton = BuildTextButton(header.transform, "CloseBtn", "<color=#FF6666>x</color>", 0.86f, 0.98f, (UnityAction)ClosePanel);
        closeButton.GetComponent<Image>()!.color = Color.clear;
    }

    static void BuildTabs()
    {
        var tabs = MakeChild("Tabs", _panel!.transform);
        tabs.AddComponent<Image>().color = TabsBg;
        Stretch(tabs, 0, 0, PANEL_H - HEADER_H - TAB_H, HEADER_H);

        var privateButton = BuildTextButton(tabs.transform, "PrivateBtn", "Private", 0.02f, 0.245f, (UnityAction)OnPrivateTabClicked);
        privateButton.GetComponent<Image>()!.color = ButtonBg;

        var publicButton = BuildTextButton(tabs.transform, "PublicBtn", "Public", 0.255f, 0.48f, (UnityAction)OnPublicTabClicked);
        publicButton.GetComponent<Image>()!.color = ButtonBg;

        var waypointsButton = BuildTextButton(tabs.transform, "WaypointsBtn", "Waypoints", 0.49f, 0.715f, (UnityAction)OnWaypointsTabClicked);
        waypointsButton.GetComponent<Image>()!.color = ButtonBg;

        var playerButton = BuildTextButton(tabs.transform, "PlayerBtn", "Player", 0.725f, 0.98f, (UnityAction)OnPlayerTabClicked);
        playerButton.GetComponent<Image>()!.color = ButtonBg;
    }

    static void BuildCards()
    {
        _cardsRoot = MakeChild("Cards", _panel!.transform);
        Stretch(_cardsRoot, 8, 8, NAV_H + 6, HEADER_H + TAB_H + 6);

        for (int index = 0; index < MAX_CARDS; index++)
            _cards.Add(new TeleportCard(_cardsRoot.transform, _layer, index, CARD_H, CARD_GAP));
    }

    static void BuildPlayerForm()
    {
        _playerForm = MakeChild("PlayerForm", _panel!.transform);
        Stretch(_playerForm, 12, 12, NAV_H + 10, HEADER_H + TAB_H + 10);
        _playerForm.AddComponent<Image>().color = PlayerBg;

        var promptGO = MakeChild("PlayerPrompt", _playerForm.transform);
        var prompt = promptGO.AddComponent<TextMeshProUGUI>();
        prompt.text = "Enter player name";
        prompt.fontSize = 12f;
        prompt.alignment = TextAlignmentOptions.Left;
        prompt.enableWordWrapping = false;
        Frac(promptGO, 0.05f, 0.78f, 0.95f, 0.95f);

        var inputRoot = MakeChild("PlayerInput", _playerForm.transform);
        inputRoot.AddComponent<Image>().color = InputBg;
        Frac(inputRoot, 0.05f, 0.56f, 0.95f, 0.76f);
        _playerInputRoot = inputRoot.GetComponent<RectTransform>();

        var textArea = MakeChild("TextArea", inputRoot.transform);
        Stretch(textArea, 10, 10, 4, 4);

        var placeholderGO = MakeChild("Placeholder", textArea.transform);
        var placeholder = placeholderGO.AddComponent<TextMeshProUGUI>();
        placeholder.text = "Player name";
        placeholder.fontSize = 12f;
        placeholder.alignment = TextAlignmentOptions.Left;
        placeholder.enableWordWrapping = false;
        placeholder.color = TextMuted;
        FillParent(placeholderGO);

        var textGO = MakeChild("Text", textArea.transform);
        var text = textGO.AddComponent<TextMeshProUGUI>();
        text.text = string.Empty;
        text.fontSize = 12f;
        text.alignment = TextAlignmentOptions.Left;
        text.enableWordWrapping = false;
        text.color = TextPrimary;
        FillParent(textGO);

        _playerNameField = inputRoot.AddComponent<TMP_InputField>();
        _playerNameField.textViewport = textArea.GetComponent<RectTransform>();
        _playerNameField.textComponent = text;
        _playerNameField.placeholder = placeholder;
        _playerNameField.lineType = TMP_InputField.LineType.SingleLine;
        _playerNameField.contentType = TMP_InputField.ContentType.Standard;
        _playerNameField.targetGraphic = inputRoot.GetComponent<Image>();

        var teleportButton = BuildTextButton(_playerForm.transform, "PlayerTeleportBtn", "Teleport", 0.05f, 0.46f, (UnityAction)OnPlayerTeleportClicked);
        Frac(teleportButton, 0.05f, 0.28f, 0.46f, 0.48f);
        teleportButton.GetComponent<Image>()!.color = ButtonBg;

        var askButton = BuildTextButton(_playerForm.transform, "PlayerAskBtn", "Accept", 0.54f, 0.95f, (UnityAction)OnAskToTpClicked);
        Frac(askButton, 0.54f, 0.28f, 0.95f, 0.48f);
        askButton.GetComponent<Image>()!.color = ButtonBg;

        var hintGO = MakeChild("PlayerHint", _playerForm.transform);
        _playerHintLabel = hintGO.AddComponent<TextMeshProUGUI>();
        _playerHintLabel.text = "Teleport sends .stp tpr [name]. Accept sends .stp tpa [name].";
        _playerHintLabel.fontSize = 10f;
        _playerHintLabel.alignment = TextAlignmentOptions.TopLeft;
        _playerHintLabel.enableWordWrapping = true;
        _playerHintLabel.color = TextSecondary;
        Frac(hintGO, 0.05f, 0.05f, 0.50f, 0.24f);

        _playerForm.SetActive(false);
    }

    static void BuildPlayerListPanel()
    {
        _playerListPanel = new GameObject("PlayerListPanel");
        AttachToCanvas(_playerListPanel);

        var rt = _playerListPanel.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(PANEL_W * 0.5f + PLAYER_LIST_W * 0.5f + 14f, 0f);
        rt.sizeDelta = new Vector2(PLAYER_LIST_W, PLAYER_LIST_H);

        _playerListPanel.AddComponent<Image>().color = PanelBg;

        var header = MakeChild("ListHeader", _playerListPanel.transform);
        header.AddComponent<Image>().color = HeaderBg;
        Stretch(header, 0, 0, PLAYER_LIST_H - HEADER_H, 0);

        var title = MakeChild("ListTitle", header.transform).AddComponent<TextMeshProUGUI>();
        title.text = "Online Players";
        title.fontSize = 12f;
        title.alignment = TextAlignmentOptions.Left;
        title.enableWordWrapping = false;
        Frac(title.gameObject, 0.05f, 0.1f, 0.80f, 0.9f, 4, 0, 2, 2);

        var closeBtn = BuildTextButton(header.transform, "ListCloseBtn", "<", 0.84f, 0.98f, (UnityAction)OnListPlayersClicked);
        closeBtn.GetComponent<Image>()!.color = Color.clear;

        var rowsRoot = MakeChild("ListRows", _playerListPanel.transform);
        Stretch(rowsRoot, 8, 8, 58, 42);

        _playerListRows.Clear();
        for (int i = 0; i < PLAYER_LIST_ROWS; i++)
        {
            var rowGO = MakeChild($"PlayerRow_{i}", rowsRoot.transform);
            var rowRT = rowGO.GetComponent<RectTransform>();
            rowRT.anchorMin = new Vector2(0f, 1f);
            rowRT.anchorMax = new Vector2(1f, 1f);
            rowRT.pivot = new Vector2(0.5f, 1f);
            rowRT.anchoredPosition = new Vector2(0f, -(i * 17f));
            rowRT.sizeDelta = new Vector2(0f, 15f);

            var rowBg = rowGO.AddComponent<Image>();
            rowBg.color = CardBg;

            var rowText = MakeChild($"PlayerRowTxt_{i}", rowGO.transform).AddComponent<TextMeshProUGUI>();
            rowText.fontSize = 10f;
            rowText.alignment = TextAlignmentOptions.Left;
            rowText.enableWordWrapping = false;
            rowText.color = TextPrimary;
            FillParent(rowText.gameObject);

            _playerListRows.Add(rowText);
        }

        var footer = MakeChild("ListFooter", _playerListPanel.transform);
        footer.AddComponent<Image>().color = HeaderBg;
        Stretch(footer, 0, 0, 0, 40);

        var prevBtn = BuildTextButton(footer.transform, "ListPrevBtn", "< Prev", 0.03f, 0.18f, (UnityAction)OnPlayerListPrevPage);
        prevBtn.GetComponent<Image>()!.color = ButtonBg;

        var pageGO = MakeChild("ListPageLabel", footer.transform);
        _playerListPageLabel = pageGO.AddComponent<TextMeshProUGUI>();
        _playerListPageLabel.fontSize = 10f;
        _playerListPageLabel.alignment = TextAlignmentOptions.Center;
        _playerListPageLabel.enableWordWrapping = false;
        _playerListPageLabel.color = TextPrimary;
        Frac(pageGO, 0.22f, 0.18f, 0.78f, 0.82f);

        var nextBtn = BuildTextButton(footer.transform, "ListNextBtn", "Next >", 0.82f, 0.97f, (UnityAction)OnPlayerListNextPage);
        nextBtn.GetComponent<Image>()!.color = ButtonBg;

        footer.transform.SetAsLastSibling();

        _playerListPanel.SetActive(false);
        _playerListOpen = false;
        _playerListPage = 0;
    }

    static void BuildNav()
    {
        _navBar = MakeChild("Nav", _panel!.transform);
        _navBar.AddComponent<Image>().color = TabsBg;
        Stretch(_navBar, 0, 0, 0, PANEL_H - NAV_H);

        var prevButton = BuildTextButton(_navBar.transform, "PrevBtn", "< Prev", 0.03f, 0.28f, (UnityAction)OnPrevPage);
        prevButton.GetComponent<Image>()!.color = ButtonBg;

        var pageGO = MakeChild("PageLabel", _navBar.transform);
        _pageLabel = pageGO.AddComponent<TextMeshProUGUI>();
        _pageLabel.fontSize = 10f;
        _pageLabel.alignment = TextAlignmentOptions.Center;
        _pageLabel.enableWordWrapping = false;
        Frac(pageGO, 0.30f, 0.12f, 0.70f, 0.88f);

        var nextButton = BuildTextButton(_navBar.transform, "NextBtn", "Next >", 0.72f, 0.97f, (UnityAction)OnNextPage);
        nextButton.GetComponent<Image>()!.color = ButtonBg;
    }

    static void OnIconClicked()
    {
        if (_panelOpen) ClosePanel();
        else OpenPanel();
    }

    static void OpenPanel()
    {
        _panelOpen = true;
        _activeCategory = TeleportCategory.Private;
        _page = 0;
        _panel?.SetActive(true);
        RequestTeleportList();
    }

    static void ClosePanel()
    {
        _panelOpen = false;
        _panel?.SetActive(false);
        _playerListOpen = false;
        _playerListPage = 0;
        _playerListPanel?.SetActive(false);
        ClearAndUnlockPlayerInput();
        TeleportDataService.CancelAwaiting();
    }

    static void OnPublicTabClicked()
    {
        ClearAndUnlockPlayerInput();
        _playerListOpen = false;
        _playerListPanel?.SetActive(false);
        _activeCategory = TeleportCategory.Public;
        _page = 0;
        RefreshPage();
    }

    static void OnPrivateTabClicked()
    {
        ClearAndUnlockPlayerInput();
        _playerListOpen = false;
        _playerListPanel?.SetActive(false);
        _activeCategory = TeleportCategory.Private;
        _page = 0;
        RefreshPage();
    }

    static void OnPlayerTabClicked()
    {
        _activeCategory = TeleportCategory.Player;
        _page = 0;
        _playerListPage = 0;
        RefreshOnlinePlayerCache();
        RefreshPage();
    }

    static void OnWaypointsTabClicked()
    {
        ClearAndUnlockPlayerInput();
        _playerListOpen = false;
        _playerListPanel?.SetActive(false);
        ClosePanel();
        Core.StartCoroutine(SendWaypointsCommandAfterDelay());
    }

    static void OnListPlayersClicked()
    {
        if (!_panelOpen || _activeCategory != TeleportCategory.Player || _playerListPanel == null)
            return;

        _playerListOpen = !_playerListOpen;
        _playerListPanel.SetActive(_playerListOpen);

        if (_playerListOpen)
        {
            if (_discoveredOnlinePlayers.Count == 0)
                TryPrimeSocialMenuRoster();

            RefreshPlayerList();
        }
        else
            _discoveredOnlinePlayers.Clear();
    }

    internal static void RegisterSocialMenuMember(string? playerName)
    {
        AddDiscoveredPlayer(playerName);
    }

    static void AddDiscoveredPlayer(string? playerName)
    {
        string normalized = NormalizeListEntry(playerName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(normalized)) return;

        if (_discoveredOnlinePlayers.Any(existing => existing.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
            return;

        _discoveredOnlinePlayers.Add(normalized);

        if (_playerListOpen)
            RefreshPlayerList();
    }

    static void OnPlayerTeleportClicked()
    {
        _holdPlayerSuppression = true;
        DeactivatePlayerInput();

        string target = GetPlayerTabInput();
        if (string.IsNullOrWhiteSpace(target))
        {
            SetPlayerHint("Enter a player name first.");
            _holdPlayerSuppression = false;
            return;
        }

        StartMapInputCooldown();
        Core.StartCoroutine(SendCommandAfterDelay($".stp tpr {target}", $"Sent: .stp tpr {target}", $"[ScarletTeleportGUI] Player teleport requested: {target}"));
    }

    static void OnAskToTpClicked()
    {
        _holdPlayerSuppression = true;
        DeactivatePlayerInput();

        string target = GetPlayerTabInput();
        if (string.IsNullOrWhiteSpace(target))
        {
            SetPlayerHint("Enter a player name first.");
            _holdPlayerSuppression = false;
            return;
        }

        StartMapInputCooldown();
        Core.StartCoroutine(SendCommandAfterDelay($".stp tpa {target}", $"Sent: .stp tpa {target}", $"[ScarletTeleportGUI] Accept teleport sent for: {target}"));
    }

    static void OnPrevPage()
    {
        if (_page <= 0) return;
        _page--;
        RefreshPage();
    }

    static void OnNextPage()
    {
        int count = CurrentList.Count;
        int maxPage = Math.Max(0, (count - 1) / MAX_CARDS);
        if (_page >= maxPage) return;
        _page++;
        RefreshPage();
    }

    static void RequestTeleportList()
    {
        TeleportDataService.BeginAwaitingTeleportList();
        CommandSender.Send(".stp ltp");
        RefreshPage();
    }

    static IReadOnlyList<string> CurrentList => _activeCategory switch
    {
        TeleportCategory.Private => TeleportDataService.VisiblePrivateTeleports,
        TeleportCategory.Public => TeleportDataService.VisiblePublicTeleports,
        TeleportCategory.Player => TeleportDataService.VisiblePlayerTeleports,
        TeleportCategory.Waypoints => TeleportDataService.VisibleWaypoints,
        _ => TeleportDataService.VisiblePrivateTeleports,
    };

    static string CurrentCategoryLabel => _activeCategory switch
    {
        TeleportCategory.Private => "Private",
        TeleportCategory.Player => "Player",
        TeleportCategory.Waypoints => "Waypoints",
        _ => "Private",
    };

    static void RefreshPage()
    {
        if (_activeCategory == TeleportCategory.Player)
        {
            _cardsRoot?.SetActive(false);
            _navBar?.SetActive(false);
            _playerForm?.SetActive(true);
            _playerListPanel?.SetActive(_playerListOpen);

            if (_titleLabel != null)
                _titleLabel.SetText("Scarlet Teleports - Player");

            if (_playerHintLabel != null && string.IsNullOrWhiteSpace(_playerHintLabel.text))
                _playerHintLabel.SetText("Teleport sends .stp tpr [name]. Accept sends .stp tpa [name].");

            ActivatePlayerInput();

            if (_playerListOpen)
                RefreshPlayerList();

            return;
        }

        DeactivatePlayerInput();
        _playerForm?.SetActive(false);
        _playerListPanel?.SetActive(false);
        _cardsRoot?.SetActive(true);
        _navBar?.SetActive(true);

        string category = CurrentCategoryLabel;
        var teleports = CurrentList;
        int total = teleports.Count;
        int maxPage = Math.Max(0, (total - 1) / MAX_CARDS);
        _page = Math.Min(_page, maxPage);
        int start = _page * MAX_CARDS;
        int shown = Math.Min(MAX_CARDS, Math.Max(0, total - start));

        if (_titleLabel != null)
        {
            string suffix = TeleportDataService.AwaitingTeleportList ? " (loading)" : string.Empty;
            _titleLabel.SetText($"Scarlet Teleports - {category}{suffix}");
        }

        if (_pageLabel != null)
        {
            if (total > 0)
                _pageLabel.SetText($"<color=#D8C9C0>{category} {start + 1}-{start + shown} of {total}</color>");
            else if (TeleportDataService.AwaitingTeleportList)
                _pageLabel.SetText($"<color=#D8C9C0>Loading {category.ToLowerInvariant()} teleports...</color>");
            else
                _pageLabel.SetText($"<color=#D8C9C0>No {category.ToLowerInvariant()} teleports found</color>");
        }

        for (int index = 0; index < _cards.Count; index++)
        {
            int itemIndex = start + index;
            if (itemIndex < total)
            {
                _cards[index].Update(teleports[itemIndex], itemIndex + 1, category, OnTeleportClicked);
                _cards[index].SetVisible(true);
            }
            else
            {
                _cards[index].SetVisible(false);
            }
        }
    }

    static void OnTeleportClicked(string teleportName)
    {
        if (string.IsNullOrWhiteSpace(teleportName)) return;

        CommandSender.Send($".stp tp {teleportName}");
        Core.Log.LogInfo($"[ScarletTeleportGUI] Teleport requested: {teleportName}");
    }

    public static void ResetState()
    {
        _killSwitch = true;
        _ready = false;
        _panelOpen = false;
        _activeCategory = TeleportCategory.Private;
        _page = 0;
        _holdPlayerSuppression = false;
        _suppressionGraceUntil = 0f;
        _blockMapInputUntil = 0f;
        _forceSuppressUntil = 0f;

        Destroy(_iconButton);
        _iconButton = null;

        Destroy(_panel);
        _panel = null;

        _titleLabel = null;
        _pageLabel = null;
        _cardsRoot = null;
        _playerForm = null;
        _playerListPanel = null;
        _navBar = null;
        _playerNameField = null;
        _playerInputRoot = null;
        _playerHintLabel = null;
        _playerListRows.Clear();
        _discoveredOnlinePlayers.Clear();
        _playerListOpen = false;
        _cards.Clear();

        TeleportDataService.Reset();
        CommandSender.Reset();

        Core.Log.LogInfo("[ScarletTeleportGUI] State reset.");
    }

    static GameObject BuildTextButton(Transform parent, string name, string text, float x0, float x1, UnityAction onClick)
    {
        var buttonGO = MakeChild(name, parent);
        buttonGO.AddComponent<Image>();
        Frac(buttonGO, x0, 0.12f, x1, 0.88f, 0, 0, 2, 2);

        var textGO = MakeChild($"{name}_Label", buttonGO.transform);
        var tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 11f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        FillParent(textGO);

        buttonGO.AddComponent<Button>().onClick.AddListener(onClick);
        return buttonGO;
    }

    static void AttachToCanvas(GameObject go)
    {
        go.layer = _layer;
        UnityEngine.Object.DontDestroyOnLoad(go);
        SceneManager.MoveGameObjectToScene(go, SceneManager.GetSceneByName("VRisingWorld"));
        go.transform.SetParent(_bottomBarCanvas!.transform, false);
    }

    static GameObject MakeChild(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.layer = _layer;
        go.AddComponent<RectTransform>();
        go.transform.SetParent(parent, false);
        return go;
    }

    static void Stretch(GameObject go, float left, float right, float bottom, float top)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    static void Frac(GameObject go, float x0, float y0, float x1, float y1, float pl = 0, float pr = 0, float pb = 0, float pt = 0)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(x0, y0);
        rt.anchorMax = new Vector2(x1, y1);
        rt.offsetMin = new Vector2(pl, pb);
        rt.offsetMax = new Vector2(-pr, -pt);
    }

    static void FillParent(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static void Destroy(GameObject? go)
    {
        if (go != null) UnityEngine.Object.Destroy(go);
    }

    static string GetPlayerTabInput()
        => _playerNameField?.text?.Trim() ?? string.Empty;

    static void SetPlayerHint(string message)
    {
        if (_playerHintLabel != null)
            _playerHintLabel.SetText(message);
    }

    static void ActivatePlayerInput()
    {
        if (_playerNameField == null || !_panelOpen || _activeCategory != TeleportCategory.Player) return;

        EventSystem.current?.SetSelectedGameObject(_playerNameField.gameObject);
        _playerNameField.Select();
        _playerNameField.ActivateInputField();
    }

    static void DeactivatePlayerInput(bool applyGrace = true)
    {
        if (_playerNameField == null) return;

        bool wasFocused = _playerNameField.isFocused;

        if (EventSystem.current?.currentSelectedGameObject == _playerNameField.gameObject)
            EventSystem.current.SetSelectedGameObject(null);

        _playerNameField.DeactivateInputField();

        if (wasFocused && applyGrace)
        {
            Input.ResetInputAxes();
            _suppressionGraceUntil = Time.realtimeSinceStartup + 0.25f;
        }
    }

    static bool ShouldReleasePlayerInput()
    {
        if (!_panelOpen || _activeCategory != TeleportCategory.Player) return false;
        if (_playerNameField == null || !_playerNameField.isFocused) return false;
        if (!Input.GetMouseButtonDown(0)) return false;

        if (_playerInputRoot == null) return false;
        if (EventSystem.current == null) return false;

        return !RectTransformUtility.RectangleContainsScreenPoint(_playerInputRoot, Input.mousePosition, null);
    }

    static System.Collections.IEnumerator SendWaypointsCommandAfterDelay()
    {
        _holdPlayerSuppression = true;
        yield return _actionDelay;
        CommandSender.Send(".stp wp");
        _holdPlayerSuppression = false;
    }

    static System.Collections.IEnumerator SendCommandAfterDelay(string command, string hintMessage, string logMessage)
    {
        yield return _actionDelay;
        CommandSender.Send(command);
        SetPlayerHint(hintMessage);
        Core.Log.LogInfo(logMessage);
        UnlockPlayerSuppression(clearCooldowns: false);
    }

    static void UnlockPlayerSuppression(bool clearCooldowns = true)
    {
        _holdPlayerSuppression = false;
        _suppressionGraceUntil = 0f;

        if (clearCooldowns)
        {
            _blockMapInputUntil = 0f;
            _forceSuppressUntil = 0f;
        }

        DeactivatePlayerInput(false);
    }

    static void ClearAndUnlockPlayerInput()
    {
        if (_playerNameField != null)
            _playerNameField.text = string.Empty;

        UnlockPlayerSuppression();
    }

    static void StartMapInputCooldown()
    {
        _blockMapInputUntil = Time.realtimeSinceStartup + 0.75f;
        _forceSuppressUntil = Time.realtimeSinceStartup + 0.75f;
    }

    static void RefreshPlayerList()
    {
        if (_playerListRows.Count == 0) return;

        List<string> names = GetOnlinePlayerNames();

        int pageSize = _playerListRows.Count;
        int maxPage = Math.Max(0, (names.Count - 1) / pageSize);
        _playerListPage = Math.Clamp(_playerListPage, 0, maxPage);
        int startIndex = _playerListPage * pageSize;

        for (int i = 0; i < _playerListRows.Count; i++)
        {
            int nameIndex = startIndex + i;
            if (nameIndex < names.Count)
                _playerListRows[i].SetText($"{nameIndex + 1}. {names[nameIndex]}");
            else
                _playerListRows[i].SetText(string.Empty);
        }

        if (_playerListPageLabel != null)
        {
            _playerListPageLabel.SetText($"Page {_playerListPage + 1}/{maxPage + 1}");
        }
    }

    static void OnPlayerListPrevPage()
    {
        if (_playerListPage <= 0) return;
        _playerListPage--;
        RefreshPlayerList();
    }

    static void OnPlayerListNextPage()
    {
        _playerListPage++;
        RefreshPlayerList();
    }

    static List<string> GetOnlinePlayerNames()
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string localPlayerName = GetLocalPlayerName();
        bool logDiagnostics = !_loggedPlayerSourceDiagnostics;

        if (logDiagnostics)
        {
            Core.Log.LogInfo($"[ScarletTeleportGUI] Player source diagnostics start: local='{localPlayerName}', cached={_discoveredOnlinePlayers.Count}");
        }

        CollectOnlinePlayerNames(names, seen, localPlayerName, logDiagnostics);

        // BloodCraft-style source: names captured from Clan/Social menu callbacks.
        foreach (string cached in _discoveredOnlinePlayers)
        {
            if (string.IsNullOrWhiteSpace(cached)) continue;
            if (IsLocalPlayerName(cached, localPlayerName)) continue;
            if (!seen.Add(cached)) continue;
            names.Add(cached);
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);

        if (names.Count == 0)
            names.Add("No other players online");

        if (logDiagnostics)
            _loggedPlayerSourceDiagnostics = true;

        return names;
    }

    static void RefreshOnlinePlayerCache()
    {
        try
        {
            string localPlayerName = GetLocalPlayerName();
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectOnlinePlayerNames(names, seen, localPlayerName, false);
            bool changed = false;

            foreach (string name in names)
            {
                if (_discoveredOnlinePlayers.Any(existing => existing.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    continue;

                _discoveredOnlinePlayers.Add(name);
                changed = true;
            }

            if (changed && _playerListOpen)
                RefreshPlayerList();
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[ScarletTeleportGUI] Could not refresh online player cache: {ex.Message}");
        }
    }

    static void TryPrimeSocialMenuRoster()
    {
        if (_socialMenuPrimeInProgress) return;
        if (Time.realtimeSinceStartup < _nextSocialMenuPrimeAttempt) return;

        ClanMenu? clanMenu = UnityEngine.Object.FindObjectOfType<ClanMenu>(true);
        if (clanMenu == null) return;

        Core.StartCoroutine(PrimeSocialMenuRoster(clanMenu.gameObject));
    }

    static System.Collections.IEnumerator PrimeSocialMenuRoster(GameObject socialMenuGO)
    {
        if (_socialMenuPrimeInProgress) yield break;

        _socialMenuPrimeInProgress = true;
        _nextSocialMenuPrimeAttempt = Time.realtimeSinceStartup + 10f;

        bool wasActive = socialMenuGO.activeSelf;

        try
        {
            ClanMenu? clanMenu = socialMenuGO.GetComponent<ClanMenu>();
            if (clanMenu != null)
                TryInvokeClanMenuPrimeMethods(clanMenu);

            if (!wasActive)
                socialMenuGO.SetActive(true);

            float endTime = Time.realtimeSinceStartup + 2.0f;
            while (Time.realtimeSinceStartup < endTime)
            {
                TryInvokeSocialMenuSystems();
                RefreshOnlinePlayerCache();

                if (_discoveredOnlinePlayers.Count > 0)
                    break;

                yield return new WaitForSecondsRealtime(0.1f);
            }

            if (_playerListOpen)
                RefreshPlayerList();
        }
        finally
        {
            if (!wasActive && socialMenuGO != null)
                socialMenuGO.SetActive(false);

            _socialMenuPrimeInProgress = false;
        }
    }

    static void TryInvokeSocialMenuSystems()
    {
        string[] candidates =
        {
            "ProjectM.Sequencer.HandleOpenSocialMenuSystem, ProjectM",
            "ProjectM.HandleOpenSocialMenuSystem, ProjectM",
        };

        foreach (string candidate in candidates)
        {
            if (Core.SystemService.TryUpdateSystem(candidate))
                Core.Log.LogInfo($"[ScarletTeleportGUI] Social menu system updated: {candidate}");
        }
    }

    static void TryInvokeClanMenuPrimeMethods(ClanMenu clanMenu)
    {
        string[] candidateMethodNames =
        {
            "InitializeUI",
            "RefreshUI",
            "Refresh",
            "UpdateUI",
            "Open",
            "Show",
            "OpenMenu",
            "Populate",
            "PopulateList",
        };

        Type type = clanMenu.GetType();
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

        foreach (string methodName in candidateMethodNames)
        {
            var method = type.GetMethod(methodName, flags, null, Type.EmptyTypes, null);
            if (method == null || method.ReturnType != typeof(void))
                continue;

            try
            {
                method.Invoke(clanMenu, null);
            }
            catch
            {
                // Ignore reflection failures and keep the prime best-effort.
            }
        }
    }

    static void CollectOnlinePlayerNames(List<string> names, HashSet<string> seen, string localPlayerName, bool logDiagnostics)
    {
        TryCollectClanTeamPlayers(names, seen, localPlayerName);
        TryCollectClanMemberStatusPlayers(names, seen, localPlayerName);

        // Primary source for all connected players.
        try
        {
            Entity localUser = Core.LocalUser;
            Entity localCharacter = Core.LocalCharacter;
            EntityQuery playerQuery = Core.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerCharacter>());
            NativeArray<Entity> players = playerQuery.ToEntityArray(Allocator.Temp);

            if (logDiagnostics)
                Core.Log.LogInfo($"[ScarletTeleportGUI] Query PlayerCharacter: entities={players.Length}");

            try
            {
                foreach (Entity playerEntity in players)
                {
                    if (!playerEntity.Exists()) continue;
                    if (localCharacter.Exists() && playerEntity == localCharacter) continue;

                    PlayerCharacter playerData = playerEntity.Read<PlayerCharacter>();
                    Entity userEntity = playerData.UserEntity;
                    User userData = userEntity.Exists() ? userEntity.Read<User>() : default;

                    string name = string.Empty;
                    if (userEntity.Exists())
                        name = NormalizeListEntry(GetUserDisplayName(userData));

                    if (string.IsNullOrWhiteSpace(name))
                        name = NormalizeListEntry(playerData.Name.Value);

                    if (logDiagnostics)
                        Core.Log.LogInfo($"[ScarletTeleportGUI] PlayerCharacter entity {playerEntity.Index} userExists={userEntity.Exists()} name='{name}'");

                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (IsLocalPlayerName(name, localPlayerName)) continue;
                    if (!seen.Add(name)) continue;

                    names.Add(name);
                }
            }
            finally
            {
                players.Dispose();
            }
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[ScarletTeleportGUI] Could not query PlayerCharacter list: {ex.Message}");
        }

        // Supplement with connected user snapshot to catch edge cases.
        try
        {
            Entity localUser = Core.LocalUser;
            EntityQuery userQuery = Core.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<User>());
            NativeArray<Entity> users = userQuery.ToEntityArray(Allocator.Temp);

            if (logDiagnostics)
                Core.Log.LogInfo($"[ScarletTeleportGUI] Query User: entities={users.Length}");

            try
            {
                foreach (Entity userEntity in users)
                {
                    if (!userEntity.Exists()) continue;
                    if (localUser.Exists() && userEntity == localUser) continue;

                    User userData = userEntity.Read<User>();
                    string name = NormalizeListEntry(GetUserDisplayName(userData));

                    if (logDiagnostics)
                        Core.Log.LogInfo($"[ScarletTeleportGUI] User entity {userEntity.Index} name='{name}'");

                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (IsLocalPlayerName(name, localPlayerName)) continue;
                    if (!seen.Add(name)) continue;

                    names.Add(name);
                }
            }
            finally
            {
                users.Dispose();
            }
        }
        catch
        {
            // Optional source only.
        }
    }

    static void TryCollectClanMemberStatusPlayers(List<string> names, HashSet<string> seen, string localPlayerName)
    {
        TryCollectBufferPlayers<ProjectM.ClanMemberStatus>(names, seen, localPlayerName);
        TryCollectBufferPlayers<ProjectM.Network.Snapshot_ClanMemberStatus>(names, seen, localPlayerName);
    }

    static void TryCollectClanTeamPlayers(List<string> names, HashSet<string> seen, string localPlayerName)
    {
        try
        {
            EntityQuery query = Core.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ProjectM.ClanTeam>());
            NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);

            if (!_loggedPlayerSourceDiagnostics)
                Core.Log.LogInfo($"[ScarletTeleportGUI] Query ClanTeam: entities={entities.Length}");

            try
            {
                foreach (Entity entity in entities)
                {
                    if (!entity.Exists()) continue;

                    if (Core.EntityManager.TryGetBuffer<ProjectM.ClanMemberStatus>(entity, out DynamicBuffer<ProjectM.ClanMemberStatus> clanBuffer))
                        CollectClanBufferEntries(entity, clanBuffer, names, seen, localPlayerName, "ClanMemberStatus");

                    if (Core.EntityManager.TryGetBuffer<ProjectM.Network.Snapshot_ClanMemberStatus>(entity, out DynamicBuffer<ProjectM.Network.Snapshot_ClanMemberStatus> snapshotBuffer))
                        CollectClanBufferEntries(entity, snapshotBuffer, names, seen, localPlayerName, "Snapshot_ClanMemberStatus");
                }
            }
            finally
            {
                entities.Dispose();
            }
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[ScarletTeleportGUI] Could not query ClanTeam list: {ex.Message}");
        }
    }

    static void CollectClanBufferEntries<TBuffer>(Entity entity, DynamicBuffer<TBuffer> buffer, List<string> names, HashSet<string> seen, string localPlayerName, string bufferLabel) where TBuffer : struct
    {
        if (!_loggedPlayerSourceDiagnostics)
            Core.Log.LogInfo($"[ScarletTeleportGUI] {bufferLabel} entity {entity.Index} bufferCount={buffer.Length}");

        foreach (TBuffer bufferEntry in buffer)
        {
            if (!TryExtractPlayerEntry(bufferEntry, out string name, out bool? isOnline))
                continue;

            if (!_loggedPlayerSourceDiagnostics)
                Core.Log.LogInfo($"[ScarletTeleportGUI] {bufferLabel} entry name='{name}' online={(isOnline.HasValue ? isOnline.Value.ToString() : "null")}");

            if (isOnline.HasValue && !isOnline.Value)
                continue;
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (IsLocalPlayerName(name, localPlayerName)) continue;
            if (!seen.Add(name)) continue;

            names.Add(name);
        }
    }

    static void TryCollectBufferPlayers<TBuffer>(List<string> names, HashSet<string> seen, string localPlayerName) where TBuffer : struct
    {
        try
        {
            EntityQuery query = Core.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TBuffer>());
            NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);

            if (!_loggedPlayerSourceDiagnostics)
                Core.Log.LogInfo($"[ScarletTeleportGUI] Query {typeof(TBuffer).Name}: entities={entities.Length}");

            try
            {
                foreach (Entity entity in entities)
                {
                    if (!entity.Exists()) continue;
                    if (!Core.EntityManager.TryGetBuffer<TBuffer>(entity, out DynamicBuffer<TBuffer> buffer)) continue;

                    if (!_loggedPlayerSourceDiagnostics)
                        Core.Log.LogInfo($"[ScarletTeleportGUI] {typeof(TBuffer).Name} entity {entity.Index} bufferCount={buffer.Length}");

                    foreach (TBuffer bufferEntry in buffer)
                    {
                        if (!TryExtractPlayerEntry(bufferEntry, out string name, out bool? isOnline))
                            continue;

                        if (!_loggedPlayerSourceDiagnostics)
                            Core.Log.LogInfo($"[ScarletTeleportGUI] {typeof(TBuffer).Name} entry name='{name}' online={(isOnline.HasValue ? isOnline.Value.ToString() : "null")}");

                        if (isOnline.HasValue && !isOnline.Value)
                            continue;
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        if (IsLocalPlayerName(name, localPlayerName)) continue;
                        if (!seen.Add(name)) continue;

                        names.Add(name);
                    }
                }
            }
            finally
            {
                entities.Dispose();
            }
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning($"[ScarletTeleportGUI] Could not query {typeof(TBuffer).Name}: {ex.Message}");
        }
    }

    static bool TryExtractPlayerEntry<TBuffer>(TBuffer bufferEntry, out string name, out bool? isOnline) where TBuffer : struct
    {
        object boxed = bufferEntry;
        Type type = boxed.GetType();
        name = string.Empty;
        isOnline = null;

        string[] nameCandidates = ["CharacterName", "PlayerName", "PlatformName", "Name", "Username", "DisplayName"];
        foreach (string candidate in nameCandidates)
        {
            name = ReadNamedMember(type, boxed, candidate);
            if (!string.IsNullOrWhiteSpace(name))
                break;
        }

        string[] onlineCandidates = ["IsOnline", "Online", "Connected", "IsConnected", "IsActive"];
        foreach (string candidate in onlineCandidates)
        {
            var property = type.GetProperty(candidate, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (property != null && property.PropertyType == typeof(bool) && property.CanRead)
            {
                isOnline = property.GetValue(boxed) is bool value ? value : (bool?)null;
                if (isOnline.HasValue) break;
            }

            var field = type.GetField(candidate, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(bool))
            {
                isOnline = field.GetValue(boxed) is bool value ? value : (bool?)null;
                if (isOnline.HasValue) break;
            }
        }

        return !string.IsNullOrWhiteSpace(name) || isOnline.HasValue;
    }

    static string GetUserDisplayName(User user)
    {
        string characterName = NormalizeNameValue(user.CharacterName.Value);
        if (!string.IsNullOrWhiteSpace(characterName))
            return characterName;

        return ExtractUserDisplayName(user);
    }

    static string GetLocalPlayerName()
    {
        Entity localUser = Core.LocalUser;
        if (localUser.Exists())
        {
            User user = localUser.Read<User>();
            string name = NormalizeListEntry(GetUserDisplayName(user));
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }

        Entity localCharacter = Core.LocalCharacter;
        if (localCharacter.Exists() && localCharacter.Has<PlayerCharacter>())
        {
            PlayerCharacter player = localCharacter.Read<PlayerCharacter>();
            string localCharName = NormalizeListEntry(player.Name.Value);
            if (!string.IsNullOrWhiteSpace(localCharName))
                return localCharName;
        }

        return string.Empty;
    }

    static bool IsLocalPlayerName(string candidate, string localPlayerName)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(localPlayerName))
            return false;

        return candidate.Equals(localPlayerName, StringComparison.OrdinalIgnoreCase);
    }

    static string NormalizeListEntry(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        string text = NormalizeNameValue(value);
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        // Trim common decorators from command output.
        text = text.TrimStart('-', '*', '•', ' ');

        int numberedPrefix = text.IndexOf('.');
        if (numberedPrefix > 0)
        {
            bool allDigits = true;
            for (int i = 0; i < numberedPrefix; i++)
            {
                if (!char.IsDigit(text[i]))
                {
                    allDigits = false;
                    break;
                }
            }

            if (allDigits && numberedPrefix + 1 < text.Length)
                text = text[(numberedPrefix + 1)..].TrimStart();
        }

        return text.Trim();
    }

    static string ExtractUserDisplayName<TUser>(TUser userData) where TUser : struct
    {
        object boxed = userData;
        Type type = boxed.GetType();

        string[] directCandidates = ["CharacterName", "PlayerName", "PlatformName", "Name"];

        foreach (string candidate in directCandidates)
        {
            string direct = ReadNamedMember(type, boxed, candidate);
            if (!string.IsNullOrWhiteSpace(direct))
                return direct;
        }

        foreach (var field in type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
        {
            if (field.Name.IndexOf("name", StringComparison.OrdinalIgnoreCase) < 0) continue;
            string value = NormalizeNameValue(field.GetValue(boxed));
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        foreach (var property in type.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
        {
            if (!property.CanRead) continue;
            if (property.Name.IndexOf("name", StringComparison.OrdinalIgnoreCase) < 0) continue;

            string value = NormalizeNameValue(property.GetValue(boxed));
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        string platformId = ReadNamedMember(type, boxed, "PlatformId");
        return string.IsNullOrWhiteSpace(platformId) ? string.Empty : $"Player {platformId}";
    }

    static string ReadNamedMember(Type type, object boxed, string memberName)
    {
        var property = type.GetProperty(memberName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (property != null && property.CanRead)
        {
            string text = NormalizeNameValue(property.GetValue(boxed));
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        var field = type.GetField(memberName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (field != null)
        {
            string text = NormalizeNameValue(field.GetValue(boxed));
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        return string.Empty;
    }

    static string NormalizeNameValue(object? value)
    {
        if (value == null) return string.Empty;

        string text = value.ToString() ?? string.Empty;
        text = text.Replace("\0", string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        if (text.Equals("None", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        if (text.Equals("Empty", StringComparison.OrdinalIgnoreCase)) return string.Empty;

        return text;
    }
}

internal sealed class TeleportCard
{
    readonly GameObject _root;
    readonly TextMeshProUGUI _nameText;
    readonly TextMeshProUGUI _infoText;
    readonly Button _button;

    public TeleportCard(Transform parent, int layer, int index, float cardHeight, float gap)
    {
        _root = new GameObject($"TeleportCard_{index}");
        _root.layer = layer;
        _root.transform.SetParent(parent, false);

        var bg = _root.AddComponent<Image>();
        bg.color = CanvasService.CardBg;
        _button = _root.AddComponent<Button>();

        var rt = _root.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -(index * (cardHeight + gap) + 3f));
        rt.sizeDelta = new Vector2(0f, cardHeight);

        var nameGO = new GameObject("NameTxt");
        nameGO.layer = layer;
        nameGO.transform.SetParent(_root.transform, false);

        _nameText = nameGO.AddComponent<TextMeshProUGUI>();
        _nameText.fontSize = 12f;
        _nameText.alignment = TextAlignmentOptions.Left;
        _nameText.enableWordWrapping = false;

        var nameRT = nameGO.GetComponent<RectTransform>();
        nameRT.anchorMin = new Vector2(0f, 0f);
        nameRT.anchorMax = new Vector2(0.75f, 1f);
        nameRT.offsetMin = new Vector2(8f, 2f);
        nameRT.offsetMax = new Vector2(0f, -2f);

        var infoGO = new GameObject("InfoTxt");
        infoGO.layer = layer;
        infoGO.transform.SetParent(_root.transform, false);

        _infoText = infoGO.AddComponent<TextMeshProUGUI>();
        _infoText.fontSize = 10f;
        _infoText.alignment = TextAlignmentOptions.Right;
        _infoText.enableWordWrapping = false;
        _infoText.color = CanvasService.TextSecondary;

        var infoRT = infoGO.GetComponent<RectTransform>();
        infoRT.anchorMin = new Vector2(0.75f, 0f);
        infoRT.anchorMax = new Vector2(1f, 1f);
        infoRT.offsetMin = new Vector2(0f, 2f);
        infoRT.offsetMax = new Vector2(-8f, -2f);

        _root.SetActive(false);
    }

    public void Update(string teleportName, int teleportNumber, string category, Action<string> onClick)
    {
        _nameText.SetText($"<color=#E8C0B8>{teleportName}</color>");
        _infoText.SetText($"{category} #{teleportNumber}");

        _button.onClick.RemoveAllListeners();
        _button.onClick.AddListener((UnityAction)(() => onClick(teleportName)));
    }

    public void SetVisible(bool visible) => _root.SetActive(visible);
}