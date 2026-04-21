using FamBook.Utilities;
using ProjectM.UI;
using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FamBook.Services;

internal class CanvasService
{
    const float BOOK_W = 310f;
    const float BOOK_H = 380f;
    const float HEADER_H = 30f;
    const float SEP_H = 2f;
    const float NAV_H = 66f;
    const float CARD_H = 26f;
    const float CARD_GAP = 2f;
    const int MAX_CARDS = 10;

    static readonly WaitForSeconds _fastWait = new(0.1f);
    static readonly WaitForSeconds _slowWait = new(1.0f);
    static WaitForSeconds WaitForSeconds => Plugin.Eclipsed ? _fastWait : _slowWait;

    static bool _ready;
    static bool _bookOpen;
    static bool _killSwitch;

    static Canvas? _bottomBarCanvas;
    static int _layer;

    static GameObject? _iconButton;
    static GameObject? _bookPanel;
    static GameObject? _cardsContainer;
    static TextMeshProUGUI? _pageLabel;

    static readonly System.Collections.Generic.List<FamiliarCard> _cards = [];

    // UI state for showing server-listed boxes (paginated)
    static bool _showingBoxList = false;
    static int  _boxListPage = 0; // 0-based page index (10 per page)
    static int  _currentListedBoxIndex = -1; // global index into DataService.LastListedBoxNames (or -1 if unset)

    internal CanvasService(UICanvasBase canvas)
    {
        // Ensure static state from any previous instance is reset so the update loop
        // and UI build behave correctly after reconnects / scene reloads.
        _killSwitch = false;
        _ready = false;
        _bookOpen = false;
        _cards.Clear();

        _bottomBarCanvas = canvas.BottomBarParent.gameObject.GetComponent<Canvas>();
        _layer = _bottomBarCanvas.gameObject.layer;

        if (!Plugin.FamiliarsPanel) return;

        try
        {
            BuildIconButton();
            BuildBookPanel();
        }
        catch (Exception ex)
        {
            Core.Log.LogError($"[FamBook] Failed to build HUD: {ex}");
        }

        _bookOpen = false;
        _bookPanel?.SetActive(false);

        if (_iconButton != null)
        {
            _ready = true;
            Core.StartCoroutine(UpdateLoop());
        }
    }

    static IEnumerator UpdateLoop()
    {
        while (true)
        {
            if (_killSwitch) yield break;

            if (_ready)
            {
                DataService.FinalizeIfExpired();
                DataService.FinalizeListIfExpired();
n                // If we've just finalized a box list, initialize current listed index so Next/Prev open boxes correctly.
                if (_showingBoxList && _currentListedBoxIndex == -1 && DataService.LastListedBoxNames.Count > 0)
                {
                    _currentListedBoxIndex = 0;
                }

                if (_bookOpen && DataService.IsDirty)
                {
                    try
                    {
                        RefreshBookPage();
                        DataService.IsDirty = false;
                    }
                    catch (Exception ex)
                    {
                        Core.Log.LogError($"[FamBook] Update error: {ex}");
                    }
                }
            }

            yield return WaitForSeconds;
        }
    }

    static void BuildIconButton()
    {
        _iconButton = new GameObject("FamBook_Icon");
        AttachToCanvas(_iconButton);

        var rt = _iconButton.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(20f, 60f);
        rt.sizeDelta = new Vector2(76f, 28f);

        var bg = _iconButton.AddComponent<Image>();
        bg.color = new Color(0.06f, 0.06f, 0.12f, 0.85f);

        var labelGO = new GameObject("IconLabel");
        labelGO.layer = _layer;
        labelGO.transform.SetParent(_iconButton.transform, false);

        var label = labelGO.AddComponent<TextMeshProUGUI>();
        label.text = "FamBook";
        label.fontSize = 11f;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;

        var labelRT = labelGO.GetComponent<RectTransform>();
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;

        _iconButton.AddComponent<Button>().onClick.AddListener((UnityAction)OnIconClicked);
        _iconButton.SetActive(true);
    }

    static void BuildBookPanel()
    {
        _bookPanel = new GameObject("FamBook_Book");
        AttachToCanvas(_bookPanel);

        var rt = _bookPanel.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(BOOK_W, BOOK_H);

        _bookPanel.AddComponent<Image>().color = new Color(0.05f, 0.04f, 0.07f, 0.92f);

        BuildHeader();

        var sep1 = MakeChild("Sep1", _bookPanel.transform);
        sep1.AddComponent<Image>().color = new Color(0.5f, 0.4f, 0.2f, 0.7f);
        Stretch(sep1, 0, 0, BOOK_H - HEADER_H - SEP_H, HEADER_H);

        _cardsContainer = MakeChild("FamBook_Cards", _bookPanel.transform);
        _cardsContainer.AddComponent<Image>().color = Color.clear;
        Stretch(_cardsContainer, 4, 4, NAV_H + SEP_H, HEADER_H + SEP_H);

        for (int i = 0; i < MAX_CARDS; i++)
            _cards.Add(new FamiliarCard(_cardsContainer.transform, _layer, i, CARD_H, CARD_GAP));

        var sep2 = MakeChild("Sep2", _bookPanel.transform);
        sep2.AddComponent<Image>().color = new Color(0.5f, 0.4f, 0.2f, 0.7f);
        Stretch(sep2, 0, 0, NAV_H, BOOK_H - NAV_H - SEP_H);

        BuildNav();
    }

    static void BuildHeader()
    {
        var header = MakeChild("BookHeader", _bookPanel!.transform);
        header.AddComponent<Image>().color = new Color(0.08f, 0.07f, 0.10f, 0.85f);
        Stretch(header, 0, 0, BOOK_H - HEADER_H, 0);

        var titleGO = MakeChild("Title", header.transform);
        var titleTMP = titleGO.AddComponent<TextMeshProUGUI>();
        titleTMP.text = "FamBook";
        titleTMP.fontSize = 12f;
        titleTMP.alignment = TextAlignmentOptions.Left;
        titleTMP.enableWordWrapping = false;
        Frac(titleGO, 0.05f, 0.1f, 0.60f, 0.9f, 8, 0, 3, 3);

        // Boxes button (top)
        var boxesGO = MakeChild("BoxesBtn", header.transform);
        boxesGO.AddComponent<Image>().color = Color.clear;
        Frac(boxesGO, 0.60f, 0f, 0.75f, 1f, 0, 4, 2, 2);
        var boxesTMP = MakeChild("BoxesTxt", boxesGO.transform).AddComponent<TextMeshProUGUI>();
        boxesTMP.text = "Boxes";
        boxesTMP.fontSize = 12f;
        boxesTMP.alignment = TextAlignmentOptions.Center;
        boxesTMP.enableWordWrapping = false;
        FillParent(boxesTMP.gameObject);
        boxesGO.AddComponent<Button>().onClick.AddListener((UnityAction)OnBoxesClicked);

        // VBlood button (top)
        var vbloodGO = MakeChild("VBloodBtn", header.transform);
        vbloodGO.AddComponent<Image>().color = Color.clear;
        Frac(vbloodGO, 0.75f, 0f, 0.88f, 1f, 0, 4, 2, 2);
        var vbloodTMP = MakeChild("VBloodTxt", vbloodGO.transform).AddComponent<TextMeshProUGUI>();
        vbloodTMP.text = "VBlood";
        vbloodTMP.fontSize = 12f;
        vbloodTMP.alignment = TextAlignmentOptions.Center;
        vbloodTMP.enableWordWrapping = false;
        FillParent(vbloodTMP.gameObject);
        vbloodGO.AddComponent<Button>().onClick.AddListener((UnityAction)OnVBloodClicked);

        var closeGO = MakeChild("CloseBtn", header.transform);
        closeGO.AddComponent<Image>().color = Color.clear;
        Frac(closeGO, 0.88f, 0f, 1f, 1f, 0, 4, 2, 2);
        var closeTMP = MakeChild("CloseTxt", closeGO.transform).AddComponent<TextMeshProUGUI>();
        closeTMP.text = "<color=#FF6666>x</color>";
        closeTMP.fontSize = 13f;
        closeTMP.alignment = TextAlignmentOptions.Center;
        closeTMP.enableWordWrapping = false;
        FillParent(closeTMP.gameObject);
        closeGO.AddComponent<Button>().onClick.AddListener((UnityAction)CloseBook);
    }

    static void BuildNav()
    {
        var nav = MakeChild("NavBar", _bookPanel!.transform);
        nav.AddComponent<Image>().color = new Color(0.07f, 0.06f, 0.09f, 0.85f);
        Stretch(nav, 0, 0, 0, BOOK_H - NAV_H);

        var prevGO = MakeChild("PrevBtn", nav.transform);
        prevGO.AddComponent<Image>().color = new Color(0.25f, 0.18f, 0.08f, 0.9f);
        Frac(prevGO, 0.02f, 0.18f, 0.30f, 0.82f);
        var prevLbl = MakeChild("PrevLbl", prevGO.transform);
        var ptmp = prevLbl.AddComponent<TextMeshProUGUI>();
        ptmp.text = "< Prev";
        ptmp.fontSize = 11f;
        ptmp.alignment = TextAlignmentOptions.Center;
        ptmp.enableWordWrapping = false;
        FillParent(prevLbl);
        prevGO.AddComponent<Button>().onClick.AddListener((UnityAction)(() => OnPrevPage()));

        var pageGO = MakeChild("PageNum", nav.transform);
        _pageLabel = pageGO.AddComponent<TextMeshProUGUI>();
        _pageLabel.text = "";
        _pageLabel.fontSize = 10f;
        _pageLabel.alignment = TextAlignmentOptions.Center;
        _pageLabel.enableWordWrapping = false;
        Frac(pageGO, 0.32f, 0.1f, 0.68f, 0.9f);

        var nextGO = MakeChild("NextBtn", nav.transform);
        nextGO.AddComponent<Image>().color = new Color(0.25f, 0.18f, 0.08f, 0.9f);
        Frac(nextGO, 0.70f, 0.18f, 0.98f, 0.82f);
        var nextLbl = MakeChild("NextLbl", nextGO.transform);
        var ntmp = nextLbl.AddComponent<TextMeshProUGUI>();
        ntmp.text = "Next >";
        ntmp.fontSize = 11f;
        ntmp.alignment = TextAlignmentOptions.Center;
        ntmp.enableWordWrapping = false;
        FillParent(nextLbl);
        nextGO.AddComponent<Button>().onClick.AddListener((UnityAction)(() => OnNextPage()));
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

    static void OnBoxesClicked()
    {
        // Open the book and request the server's list of boxes. Responses will be intercepted and parsed.
        OpenBook();
        _showingBoxList = true;
        _boxListPage = 0;
        CommandSender.Send(".fam listboxes");
        DataService.BeginAwaitingBoxList();
    }

    static void OnVBloodClicked()
    {
        // Send a vblood command — server/mod handlers can respond as needed
        CommandSender.Send(".vblood");
        Core.Log.LogInfo("[FamBook] VBlood command sent via button.");
    }

    static void OnIconClicked()
    {
        if (_bookOpen) CloseBook();
        else OpenBook();
    }

    static void OpenBook()
    {
        _bookOpen = true;
        _bookPanel?.SetActive(true);
        // Default to showing the boxes list when opening the FamBook UI.
        _showingBoxList = true;
        _boxListPage = 0;
        CommandSender.Send(".fam listboxes");
        DataService.BeginAwaitingBoxList();
    }

    static void CloseBook()
    {
        _bookOpen = false;
        _bookPanel?.SetActive(false);
    }

    static void OnPrevPage()
    {
        if (_showingBoxList)
        {
            // If we have a maintained list, navigate boxes; otherwise page the UI.
            int total = DataService.LastListedBoxNames.Count;
            if (total > 0)
            {
                if (_currentListedBoxIndex <= 0)
                {
                    // already at first, clamp
                    _currentListedBoxIndex = 0;
                }
                else
                {
                    _currentListedBoxIndex--;
                }

                // Open the selected box
                OpenBoxByIndex(_currentListedBoxIndex);
                return;
            }

            // fallback to paging
            if (_boxListPage > 0)
            {
                _boxListPage--;
                RefreshBookPage();
            }
            return;
        }

        if (DataService.CurrentBoxIndex > 0)
        {
            DataService.CurrentBoxIndex--;
            RequestCurrentBox();
        }
    }

    static void OnNextPage()
    {
        if (_showingBoxList)
        {
            int total = DataService.LastListedBoxNames.Count;
            int maxPage = Math.Max(0, (total - 1) / MAX_CARDS);
            if (_boxListPage < maxPage)
            {
                _boxListPage++;
                RefreshBookPage();
            }
            return;
        }

        DataService.CurrentBoxIndex++;
        RequestCurrentBox();
    }

    static void RequestCurrentBox() => CommandSender.RequestBoxData(DataService.CurrentBoxIndex);

    static void RefreshBookPage()
    {
        // If we're showing the server-provided boxes list, show a paginated view of DataService.LastListedBoxNames
        if (_showingBoxList && DataService.LastListedBoxNames.Count > 0)
        {
            int total = DataService.LastListedBoxNames.Count;
            int start = _boxListPage * MAX_CARDS;
            int shown = Math.Min(MAX_CARDS, Math.Max(0, total - start));

            if (_pageLabel != null)
                _pageLabel.SetText($"<color=#CCBBAA>Boxes {start + 1}-{start + shown} of {total}</color>");

            for (int i = 0; i < _cards.Count; i++)
            {
                int idx = start + i;
                if (idx < total)
                {
                    string boxName = DataService.LastListedBoxNames[idx];
                    _cards[i].UpdateBox(boxName, idx + 1, OnBoxClicked);
                    _cards[i].SetVisible(true);
                }
                else
                {
                    _cards[i].SetVisible(false);
                }
            }

            Core.Log.LogInfo($"[FamBook] Boxes refreshed: showing {shown} of {total} boxes (page {_boxListPage + 1}).");
            return;
        }

        int boxIdx = DataService.CurrentBoxIndex;

        System.Collections.Generic.List<FamiliarEntry> familiars = [];
        string boxTitle = DataService.CurrentBoxName;
        if (string.IsNullOrEmpty(boxTitle)) boxTitle = $"Box {boxIdx + 1}";

        if (DataService.Boxes.TryGetValue(boxIdx, out BoxData? boxData))
            familiars = boxData.Familiars;

        if (_pageLabel != null)
            _pageLabel.SetText($"<color=#CCBBAA>{boxTitle}</color>");

        for (int i = 0; i < _cards.Count; i++)
        {
            if (i < familiars.Count)
            {
                _cards[i].Update(familiars[i], i + 1, OnFamiliarClicked);
                _cards[i].SetVisible(true);
            }
            else
            {
                _cards[i].SetVisible(false);
            }
        }

        Core.Log.LogInfo($"[FamBook] Book refreshed: {familiars.Count} cards shown for '{boxTitle}'.");
    }

    static void OnFamiliarClicked(int famNumber, string familiarName)
    {
        if (famNumber <= 0) return;

        DataService.BeginAwaitingBindAttempt(famNumber);
        CommandSender.Send($".fam b {famNumber}");
        Core.Log.LogInfo($"[FamBook] Clicked familiar #{famNumber} ({familiarName}) -> .fam b {famNumber}");
    }

    static void OnBoxClicked(int boxNumber, string boxName)
    {
        if (string.IsNullOrWhiteSpace(boxName)) return;

        // When a box is clicked in the boxes list, request its familiars and switch to normal book view.
        _showingBoxList = false;
        _boxListPage = 0;
        // boxName may include commas or spaces if parsing joined lines; ensure we send just the box token if present.
        // If names are like "box1" it's fine; otherwise send as-is.
        CommandSender.Send($".fam cb {boxName}");
        CommandSender.Send(".fam l");
        DataService.BeginAwaitingResponse();
        Core.Log.LogInfo($"[FamBook] Requested familiars for box '{boxName}' via button.");
    }

    public static void ResetState()
    {
        _killSwitch = true;
        _ready = false;
        _bookOpen = false;

        Destroy(_iconButton); _iconButton = null;
        Destroy(_bookPanel); _bookPanel = null;
        _cardsContainer = null;
        _cards.Clear();
        // Reset boxes UI state
        _showingBoxList = false;
        _boxListPage = 0;
        DataService.Reset();
        CommandSender.Reset();

        Core.Log.LogInfo("[FamBook] State reset.");
    }

    static void Destroy(GameObject? go)
    {
        if (go != null) UnityEngine.Object.Destroy(go);
    }
}

internal sealed class FamiliarCard
{
    readonly GameObject _root;
    readonly TextMeshProUGUI _nameText;
    readonly TextMeshProUGUI _infoText;
    readonly Button _button;

    static readonly Color NormalBg = new(0.10f, 0.09f, 0.12f, 0.55f);

    public FamiliarCard(Transform parent, int layer, int index, float cardH, float gap)
    {
        _root = new GameObject($"FamCard_{index}");
        _root.layer = layer;
        _root.transform.SetParent(parent, false);

        var bg = _root.AddComponent<Image>();
        bg.color = NormalBg;
        _button = _root.AddComponent<Button>();

        var rt = _root.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -(index * (cardH + gap) + 3f));
        rt.sizeDelta = new Vector2(0f, cardH);

        var nameGO = new GameObject("NameTxt");
        nameGO.layer = layer;
        nameGO.transform.SetParent(_root.transform, false);

        _nameText = nameGO.AddComponent<TextMeshProUGUI>();
        _nameText.fontSize = 12f;
        _nameText.alignment = TextAlignmentOptions.Left;
        _nameText.enableWordWrapping = false;

        var nameRT = nameGO.GetComponent<RectTransform>();
        nameRT.anchorMin = new Vector2(0f, 0f);
        nameRT.anchorMax = new Vector2(0.68f, 1f);
        nameRT.offsetMin = new Vector2(8f, 2f);
        nameRT.offsetMax = new Vector2(0f, -2f);

        var infoGO = new GameObject("InfoTxt");
        infoGO.layer = layer;
        infoGO.transform.SetParent(_root.transform, false);

        _infoText = infoGO.AddComponent<TextMeshProUGUI>();
        _infoText.fontSize = 11f;
        _infoText.alignment = TextAlignmentOptions.Right;
        _infoText.enableWordWrapping = false;
        _infoText.color = new Color(0.85f, 0.85f, 0.85f);

        var infoRT = infoGO.GetComponent<RectTransform>();
        infoRT.anchorMin = new Vector2(0.68f, 0f);
        infoRT.anchorMax = new Vector2(1f, 1f);
        infoRT.offsetMin = new Vector2(0f, 2f);
        infoRT.offsetMax = new Vector2(-8f, -2f);

        _root.SetActive(false);
    }

    public void Update(FamiliarEntry f, int famNumber, Action<int, string> onClick)
    {
        string shiny = f.IsShiny ? $"{f.ShinyColorTag}*</color>" : string.Empty;
        _nameText.SetText($"<color=#90EE90>{f.Name}</color>{shiny}");

        string prestige = f.Prestige > 0 ? $"[<color=#90EE90>P{f.Prestige}</color>] " : string.Empty;
        _infoText.SetText($"{prestige}Lv <color=white>{f.Level}</color>");

        _button.onClick.RemoveAllListeners();
        _button.onClick.AddListener((UnityAction)(() => onClick(famNumber, f.Name)));
    }

    // Update card for listing box names (from .fam listboxes)
    public void UpdateBox(string boxName, int boxNumber, Action<int, string> onClick)
    {
        _nameText.SetText($"<color=#90EE90>{boxName}</color>");
        _infoText.SetText($"# {boxNumber}");
        _button.onClick.RemoveAllListeners();
        _button.onClick.AddListener((UnityAction)(() => onClick(boxNumber, boxName)));
    }

    public void SetVisible(bool v) => _root.SetActive(v);
}
