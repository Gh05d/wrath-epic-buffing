using BuffIt2TheLimit.Config;
using Kingmaker;
using Kingmaker.Blueprints.Items.Equipment;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.UI.Common;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BuffIt2TheLimit {

    /// <summary>
    /// The Pearl of Power strip under the caster row of the buff details panel, for ONE caster (the only one,
    /// or the one whose portrait was clicked last): every pearl that fits that caster's slot for the selected
    /// spell by the game's rule (its level or higher), in two labelled groups —
    ///   "Equipped pearls": the caster's own quick-slot pearls, always leftmost, amber panes;
    ///   "Inventory pearls": pearls in the shared inventory, black panes.
    /// Each pane is a switch: click it green and this spell may use that kind of pearl; click again to stop.
    /// Nothing is green by default. Room for every pearl kind in the game (15) plus the five quick slots.
    /// A pane is faded while the routine would not touch it (Use Pearls of Power off; inventory pearls in combat;
    /// quick-slot pearls without "Allow equipped pearls' usage"). Spent pearls (0 until the next rest) stay listed.
    /// </summary>
    class PearlShelf {

        internal class Stock {
            public string Kind;
            public AbilityRestoreSpellSlot Restore;
            public Sprite Icon;
            public string Name;
            public int Uses;    // charges ready now: Count × Charges over the stacks
            public int Items;   // pearls carried, spent ones included
        }

        private class Cell {
            public GameObject Root;
            public CanvasGroup Fade;
            public Image Pane;
            public Image Icon;
            public TextMeshProUGUI Count;
            public TextMeshProUGUI Shadow;   // dark copy of Count, one pixel down-right
            public string Kind;
            // For the hover panel: what to say about this pearl, and whose stock it is (null = inventory)
            public string Title;
            public string Info;
            public UnitEntityData QuickSlotOwner;
        }

        private class Group {
            public GameObject Root;
            public TextMeshProUGUI Label;
            public RectTransform Icons;
            public bool FromQuickSlot;
            public readonly List<Cell> Cells = new();
        }

        private const float CellSize = 40f;
        private static readonly Color InventoryPane = new(0f, 0f, 0f, 0.55f);
        private static readonly Color EquippedPane = new(0.62f, 0.45f, 0.1f, 0.8f);
        private static readonly Color PickedPane = new(0.16f, 0.55f, 0.2f, 0.85f);
        private static readonly Color SpentIcon = new(0.45f, 0.45f, 0.45f, 1f);
        private static readonly Color SpentCount = new(0.75f, 0.75f, 0.75f, 1f);
        private static readonly Color LabelInk = new(0.2f, 0.15f, 0.1f, 1f);

        private static Sprite roundedPane;

        /// <summary>
        /// A small white rounded rectangle, built once and nine-sliced (Image.Type.Sliced) so the corners keep
        /// their radius at any size; the Image colour tints it. Anti-aliased by coverage at the corner arcs.
        /// </summary>
        private static Sprite RoundedPane {
            get {
                if (roundedPane != null) return roundedPane;
                const int size = 32;
                const float radius = 9f;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++) {
                    for (int x = 0; x < size; x++) {
                        // Distance from the pixel centre to the nearest corner-arc centre (0 inside the straight edges)
                        float px = x + 0.5f, py = y + 0.5f;
                        float cx = Mathf.Clamp(px, radius, size - radius);
                        float cy = Mathf.Clamp(py, radius, size - radius);
                        float dist = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                        float coverage = Mathf.Clamp01(radius - dist + 0.5f);
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(coverage * 255f));
                    }
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                int border = Mathf.CeilToInt(radius);
                roundedPane = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                    SpriteMeshType.FullRect, new Vector4(border, border, border, border));
                return roundedPane;
            }
        }

        private readonly RectTransform root;
        private readonly Group equippedGroup;
        private readonly Group inventoryGroup;
        private readonly Action<string, bool> onToggle;
        private readonly PearlHoverPanel hover;

        private PearlShelf(RectTransform root, Group equipped, Group inventory, Action<string, bool> onToggle) {
            this.root = root;
            equippedGroup = equipped;
            inventoryGroup = inventory;
            this.onToggle = onToggle;
            hover = new PearlHoverPanel(root);
        }

        /// <param name="parent">the casters section; the strip is centred in it, under the portraits</param>
        /// <param name="onToggle">(kind, fromQuickSlot): a pane was clicked</param>
        internal static PearlShelf Attach(Transform parent, Action<string, bool> onToggle) {
            var (rootObj, rootRect) = UIHelpers.Create("pearl-strip", parent);
            rootRect.anchorMin = new Vector2(0.5f, 0.5f);
            rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 1f);
            rootRect.anchoredPosition = Vector2.zero;
            var row = rootObj.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 18f;
            row.childAlignment = TextAnchor.UpperCenter;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            var fitter = rootObj.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var equipped = MakeGroup(rootRect, "equipped-pearls", fromQuickSlot: true);
            var inventory = MakeGroup(rootRect, "inventory-pearls", fromQuickSlot: false);

            // Above the casters scroller in draw and click order
            rootRect.SetAsLastSibling();
            rootObj.SetActive(false);
            return new PearlShelf(rootRect, equipped, inventory, onToggle);
        }

        private static Group MakeGroup(RectTransform parent, string name, bool fromQuickSlot) {
            var (groupObj, groupRect) = UIHelpers.Create(name, parent);
            var column = groupObj.AddComponent<VerticalLayoutGroup>();
            column.spacing = 2f;
            column.childAlignment = TextAnchor.UpperLeft;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = false;
            column.childForceExpandHeight = false;

            // Group caption. Only plain text properties here: the strip is inactive while it is built, and TMP
            // has no material until it is first enabled (setting outlineWidth here throws ArgumentNullException).
            var (labelObj, _) = UIHelpers.Create("label", groupRect);
            var label = labelObj.AddComponent<TextMeshProUGUI>();
            label.fontSize = 15;
            label.color = LabelInk;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;
            var labelLayout = labelObj.AddComponent<LayoutElement>();
            labelLayout.minHeight = 18f;            // height fixed, width from the text
            labelLayout.preferredHeight = 18f;
            labelLayout.layoutPriority = 2;

            var (iconsObj, iconsRect) = UIHelpers.Create("icons", groupRect);
            var icons = iconsObj.AddComponent<HorizontalLayoutGroup>();
            icons.spacing = 2f;
            icons.childAlignment = TextAnchor.UpperLeft;
            icons.childControlWidth = true;
            icons.childControlHeight = true;
            icons.childForceExpandWidth = false;
            icons.childForceExpandHeight = false;

            return new Group { Root = groupObj, Label = label, Icons = iconsRect, FromQuickSlot = fromQuickSlot };
        }

        private Cell MakeCell(Group group) {
            var (cellObj, _) = UIHelpers.Create("pearl", group.Icons);
            try {
                var cell = BuildCell(cellObj);
                group.Cells.Add(cell);
                return cell;
            } catch {
                // A half-built cell would stay in the row as an empty square; switch it off (not Destroy:
                // Show can run inside a click callback, see gotchas-ui.md).
                cellObj.SetActive(false);
                throw;
            }
        }

        private Cell BuildCell(GameObject cellObj) {
            var size = cellObj.AddComponent<LayoutElement>();
            size.minWidth = size.preferredWidth = CellSize;
            size.minHeight = size.preferredHeight = CellSize;
            var fade = cellObj.AddComponent<CanvasGroup>();

            // Rounded half-transparent pane behind the icon — the click and hover target of the cell. Offsets
            // set explicitly: a child parented by UIHelpers sits on its parent's PIVOT until told otherwise.
            var (paneObj, paneRect) = UIHelpers.Create("pane", cellObj.transform);
            paneRect.anchorMin = Vector2.zero;
            paneRect.anchorMax = Vector2.one;
            paneRect.offsetMin = Vector2.zero;
            paneRect.offsetMax = Vector2.zero;
            var pane = paneObj.AddComponent<Image>();
            pane.sprite = RoundedPane;
            pane.type = Image.Type.Sliced;
            pane.color = InventoryPane;
            pane.raycastTarget = true;

            var (iconObj, iconRect) = UIHelpers.Create("icon", cellObj.transform);
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(3f, 3f);
            iconRect.offsetMax = new Vector2(-3f, -3f);
            var icon = iconObj.AddComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;   // clicks and hover land on the pane

            // Count in the bottom-right corner, like the stack counts in the inventory: white over a dark copy
            // one pixel down-right (no TMP outline — see MakeGroup)
            TextMeshProUGUI MakeCount(string name, Vector2 shift) {
                var (textObj, textRect) = UIHelpers.Create(name, cellObj.transform);
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = new Vector2(2f, 1f) + shift;
                textRect.offsetMax = new Vector2(-3f, -1f) + shift;
                var text = textObj.AddComponent<TextMeshProUGUI>();
                text.fontSize = 14;
                text.fontStyle = FontStyles.Bold;
                text.alignment = TextAlignmentOptions.BottomRight;
                text.enableWordWrapping = false;
                text.overflowMode = TextOverflowModes.Overflow;
                text.raycastTarget = false;
                return text;
            }
            var shadow = MakeCount("count-shadow", new Vector2(1f, -1f));
            shadow.color = new Color(0f, 0f, 0f, 0.9f);
            var count = MakeCount("count", Vector2.zero);

            var cell = new Cell { Root = cellObj, Fade = fade, Pane = pane, Icon = icon, Count = count, Shadow = shadow };

            // Click and hover both on the pane (a pointer event goes to the first object up the hierarchy that
            // handles it, and the icon and count ignore raycasts). Hover opens the strip's own panel instead of
            // a game tooltip: it lists the spells this pearl kind is planned for.
            var button = paneObj.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = pane;
            button.onClick.AddListener(() => {
                if (!string.IsNullOrEmpty(cell.Kind))
                    onToggle?.Invoke(cell.Kind, InEquippedGroup(cell));
            });
            var relay = paneObj.AddComponent<PearlHoverRelay>();
            relay.Enter = () => ShowHover(cell, paneRect);
            relay.Exit = () => {
                if (hoveredCell == cell) hoveredCell = null;
                hover.Hide();
            };
            return cell;
        }

        // The pane under the pointer; Show() refreshes its panel after a click re-plans the routine
        private Cell hoveredCell;
        private RectTransform hoveredPane;

        private void ShowHover(Cell cell, RectTransform pane) {
            if (string.IsNullOrEmpty(cell.Kind)) return;
            hoveredCell = cell;
            hoveredPane = pane;
            var plan = SpellSlotLedger.Current?.AllocationsFor(cell.Kind, cell.QuickSlotOwner)
                       ?? new List<(BubbleBuff Buff, int Pearls)>();
            hover.Show(pane, cell.Icon.sprite, cell.Title, cell.Info, plan);
        }

        internal static Sprite RoundedPaneSprite => RoundedPane;

        private bool InEquippedGroup(Cell cell) => equippedGroup.Cells.Contains(cell);

        private static List<Stock> Aggregate(IEnumerable<ItemEntity> items) {
            var byKind = new Dictionary<string, Stock>();
            foreach (var item in items) {
                if (item == null || !item.IsIdentified) continue;
                if (!PearlsOfPower.TryGetRestore(item, out var restore)) continue;
                var kind = PearlsOfPower.KindId(item);
                if (!byKind.TryGetValue(kind, out var stock)) {
                    stock = new Stock { Kind = kind, Restore = restore, Icon = item.Icon, Name = ((BlueprintItemEquipmentUsable)item.Blueprint).Name };
                    byKind[kind] = stock;
                }
                stock.Uses += item.Count * Math.Max(0, item.Charges);
                stock.Items += item.Count;
            }
            return byKind.Values
                .OrderBy(s => PearlsOfPower.PearlLevel(s.Restore))
                .ThenBy(s => s.Name)
                .ToList();
        }

        /// <summary>Unequipped, identified Pearls of Power in the shared inventory, one entry per kind.</summary>
        internal static List<Stock> ReadInventory() =>
            Aggregate(Game.Instance.Player.Inventory.Where(i => i != null && i.HoldingSlot == null));

        // This caster's own quick-slot pearls, one entry per kind.
        private static List<Stock> ReadQuickSlots(UnitEntityData unit) {
            var slots = unit?.Body?.QuickSlots;
            if (slots == null) return new List<Stock>();
            return Aggregate(slots.Where(s => s != null && s.HasItem).Select(s => (ItemEntity)s.Item));
        }

        internal void Hide() {
            hover.Hide();
            root.gameObject.SetActive(false);
        }

        /// <param name="nameCaster">several casters can cast the spell: say whose pearls these are</param>
        /// <param name="top">y of the strip's top edge, relative to the centre of the casters section</param>
        internal void Show(BubbleBuff buff, UnitEntityData caster, bool nameCaster, float top, List<Stock> inventoryStock) {
            var settings = PearlsOfPower.Settings;
            if (settings == null || !settings.PearlsEnabled || buff == null || buff.IsActivatable || caster == null) {
                Hide();
                return;
            }

            // The caster's spellbook providers for this spell and the slot level each one casts it from.
            var books = buff.CasterQueue
                .Where(p => p.who == caster && SpellSlotLedger.Tracks(p))
                .Select(p => (book: p.SlottedSpell.Spellbook, level: PearlsOfPower.SlotLevel(p.SlottedSpell)))
                .Where(b => b.level >= 1)
                .ToList();
            bool Fits(Stock s) => books.Any(b => PearlsOfPower.BookAccepts(s.Restore, b.book, b.level)
                                              && PearlsOfPower.Compatible(s.Restore, b.level));
            var quickSlot = books.Count == 0 ? new List<Stock>() : ReadQuickSlots(caster).Where(Fits).ToList();
            var inventory = books.Count == 0 || inventoryStock == null ? new List<Stock>() : inventoryStock.Where(Fits).ToList();
            if (quickSlot.Count == 0 && inventory.Count == 0) {
                Hide();
                return;
            }

            bool pearlMode = buff.UsesPearls(settings);
            bool inCombat = Game.Instance.Player.IsInCombat;
            string equippedCaption = "pearls.group-equipped".i8();
            if (nameCaster) equippedCaption += $" — {caster.CharacterName}";
            Fill(equippedGroup, quickSlot, buff.PickedPearls(settings, true), pearlMode && buff.UsesEquippedPearls(settings),
                equippedCaption, caster);
            Fill(inventoryGroup, inventory, buff.PickedPearls(settings, false), pearlMode && !inCombat,
                "pearls.group-inventory".i8(), caster);

            root.anchoredPosition = new Vector2(0f, top);
            root.gameObject.SetActive(true);

            // A click re-plans and lands here with the pointer still on the pane: refresh its panel
            if (hoveredCell != null && hoveredCell.Root.activeInHierarchy)
                ShowHover(hoveredCell, hoveredPane);
        }

        private void Fill(Group group, List<Stock> stocks, HashSet<string> picked, bool active, string caption, UnitEntityData caster) {
            group.Root.SetActive(stocks.Count > 0);
            if (stocks.Count == 0) return;
            group.Label.text = caption;
            while (group.Cells.Count < stocks.Count) MakeCell(group);
            for (int i = 0; i < group.Cells.Count; i++) {
                var cell = group.Cells[i];
                if (i >= stocks.Count) {
                    cell.Root.SetActive(false);
                    cell.Kind = null;
                    continue;
                }
                var s = stocks[i];
                bool isPicked = picked.Contains(s.Kind);
                bool ready = s.Uses > 0;
                cell.Kind = s.Kind;
                cell.Pane.color = isPicked ? PickedPane : group.FromQuickSlot ? EquippedPane : InventoryPane;
                cell.Icon.sprite = s.Icon;
                cell.Icon.color = ready ? Color.white : SpentIcon;
                cell.Count.text = s.Uses.ToString();
                cell.Shadow.text = cell.Count.text;
                cell.Count.color = ready ? Color.white : SpentCount;
                cell.Fade.alpha = active ? 1f : 0.45f;
                cell.QuickSlotOwner = group.FromQuickSlot ? caster : null;
                cell.Title = s.Name;
                string stock = group.FromQuickSlot
                    ? string.Format("pearls.row-tooltip-equipped".i8(), s.Uses, caster.CharacterName)
                    : string.Format("pearls.row-tooltip".i8(), s.Uses, s.Items);
                cell.Info = stock + "\n" + (isPicked ? "pearls.picked-hint" : "pearls.unpicked-hint").i8();
                cell.Root.SetActive(true);
            }
        }
    }

    // Pointer enter/exit on a pearl pane, forwarded to the strip's hover panel.
    class PearlHoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {
        public Action Enter;
        public Action Exit;

        public void OnPointerEnter(PointerEventData eventData) {
            try { Enter?.Invoke(); } catch (Exception ex) { Main.Error(ex, "[Pearls] hover panel"); }
        }

        public void OnPointerExit(PointerEventData eventData) {
            try { Exit?.Invoke(); } catch (Exception ex) { Main.Error(ex, "[Pearls] hover panel"); }
        }

        private void OnDisable() => Exit?.Invoke();   // a pane hidden under the pointer gets no exit event
    }

    /// <summary>
    /// The hover panel of a pearl pane: a black half-transparent rounded panel above the pane with the pearl's
    /// name, its stock line and pick state, and every spell this pearl kind is planned for in the current plan
    /// (SpellSlotLedger.AllocationsFor) — the spell's icon, its metamagic frame (the game's own decoration
    /// border and colour, as the spellbook draws them) and how many of these pearls it gets.
    /// Nothing in it takes the pointer, so it never flickers or blocks a click.
    /// </summary>
    class PearlHoverPanel {
        private const float EntrySize = 40f;
        private const int MaxColumns = 6;
        private static readonly Color Backing = new(0f, 0f, 0f, 0.8f);

        private readonly RectTransform panel;
        private readonly TextMeshProUGUI title;
        private readonly TextMeshProUGUI info;
        private readonly TextMeshProUGUI caption;
        private readonly GridLayoutGroup grid;
        private readonly List<(GameObject Root, Image Icon, Image Frame, TextMeshProUGUI Count, TextMeshProUGUI Shadow)> entries = new();

        internal PearlHoverPanel(RectTransform strip) {
            var (panelObj, panelRect) = UIHelpers.Create("pearl-hover", strip);
            panel = panelRect;
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0f);
            panelObj.AddComponent<LayoutElement>().ignoreLayout = true;   // not part of the strip's row

            var backing = panelObj.AddComponent<Image>();
            backing.sprite = PearlShelf.RoundedPaneSprite;
            backing.type = Image.Type.Sliced;
            backing.color = Backing;
            backing.raycastTarget = false;

            var column = panelObj.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(10, 10, 8, 10);
            column.spacing = 4f;
            column.childAlignment = TextAnchor.UpperLeft;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = false;
            column.childForceExpandHeight = false;
            var fitter = panelObj.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Plain text properties only: the panel is built while the strip is inactive, and TMP has no
            // material before it is first enabled (no outline etc. here).
            TextMeshProUGUI Text(string name, float size, Color color, FontStyles style) {
                var (textObj, _) = UIHelpers.Create(name, panelRect);
                var text = textObj.AddComponent<TextMeshProUGUI>();
                text.fontSize = size;
                text.color = color;
                text.fontStyle = style;
                text.alignment = TextAlignmentOptions.TopLeft;
                text.enableWordWrapping = false;
                text.overflowMode = TextOverflowModes.Overflow;
                text.raycastTarget = false;
                return text;
            }
            title = Text("title", 16, Color.white, FontStyles.Bold);
            info = Text("info", 13, new Color(0.85f, 0.85f, 0.85f, 1f), FontStyles.Normal);
            caption = Text("caption", 13, new Color(0.95f, 0.85f, 0.55f, 1f), FontStyles.Normal);

            var (gridObj, _) = UIHelpers.Create("spells", panelRect);
            grid = gridObj.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(EntrySize, EntrySize);
            grid.spacing = new Vector2(4f, 4f);
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = MaxColumns;

            panelObj.SetActive(false);
        }

        private (GameObject Root, Image Icon, Image Frame, TextMeshProUGUI Count, TextMeshProUGUI Shadow) MakeEntry() {
            var (entryObj, _) = UIHelpers.Create("spell", grid.transform);
            Image Fill(string name) {
                var (obj, rect) = UIHelpers.Create(name, entryObj.transform);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                var image = obj.AddComponent<Image>();
                image.raycastTarget = false;
                return image;
            }
            var icon = Fill("icon");
            icon.preserveAspect = true;
            var frame = Fill("metamagic-frame");
            TextMeshProUGUI Number(string name, Vector2 shift) {
                var (obj, rect) = UIHelpers.Create(name, entryObj.transform);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(2f, 1f) + shift;
                rect.offsetMax = new Vector2(-3f, -1f) + shift;
                var text = obj.AddComponent<TextMeshProUGUI>();
                text.fontSize = 15;
                text.fontStyle = FontStyles.Bold;
                text.alignment = TextAlignmentOptions.BottomRight;
                text.enableWordWrapping = false;
                text.overflowMode = TextOverflowModes.Overflow;
                text.raycastTarget = false;
                return text;
            }
            var shadow = Number("count-shadow", new Vector2(1f, -1f));
            shadow.color = new Color(0f, 0f, 0f, 0.95f);
            var count = Number("count", Vector2.zero);
            count.color = Color.white;
            var entry = (entryObj, icon, frame, count, shadow);
            entries.Add(entry);
            return entry;
        }

        // The game's metamagic decoration for this spell (SpellbookKnownSpellView.SetupDecoration): the border
        // the player chose for it and its colour; none for a spell without one.
        private static bool Decoration(BubbleBuff buff, out Sprite border, out Color color) {
            border = null;
            color = Color.clear;
            var candidates = new List<AbilityData> { buff.Spell };
            candidates.AddRange(buff.CasterQueue.Select(p => p.SlottedSpell));
            foreach (var spell in candidates) {
                if (spell == null || spell.DecorationBorderNumber < 0 || spell.DecorationColorNumber < 0) continue;
                border = UIUtility.GetDecorationBorderByIndex(spell.DecorationBorderNumber);
                color = UIUtility.GetDecorationColorByIndex(spell.DecorationColorNumber);
                return border != null;
            }
            return false;
        }

        internal void Show(RectTransform pane, Sprite pearlIcon, string pearlName, string pearlInfo, List<(BubbleBuff Buff, int Pearls)> plan) {
            title.text = pearlName ?? "";
            info.text = pearlInfo ?? "";
            caption.text = (plan.Count > 0 ? "pearls.planned-for" : "pearls.planned-none").i8();

            while (entries.Count < plan.Count) MakeEntry();
            for (int i = 0; i < entries.Count; i++) {
                var entry = entries[i];
                if (i >= plan.Count) {
                    entry.Root.SetActive(false);
                    continue;
                }
                var (buff, pearls) = plan[i];
                entry.Icon.sprite = buff.Icon;
                bool framed = Decoration(buff, out var border, out var color);
                entry.Frame.gameObject.SetActive(framed);
                if (framed) {
                    entry.Frame.sprite = border;
                    entry.Frame.color = color;
                }
                entry.Count.text = pearls.ToString();
                entry.Shadow.text = entry.Count.text;
                entry.Root.SetActive(true);
            }
            grid.constraintCount = Mathf.Clamp(plan.Count, 1, MaxColumns);
            grid.gameObject.SetActive(plan.Count > 0);

            // Above the hovered pane, centred on it
            panel.gameObject.SetActive(true);
            panel.SetAsLastSibling();
            panel.position = pane.TransformPoint(new Vector3(pane.rect.center.x, pane.rect.yMax + 6f, 0f));
        }

        internal void Hide() {
            if (panel != null) panel.gameObject.SetActive(false);
        }
    }
}
