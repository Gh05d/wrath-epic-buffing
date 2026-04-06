using BuffIt2TheLimit.Config;
using BuffIt2TheLimit.Utilities;
using HarmonyLib;
using Kingmaker;
using Kingmaker.AreaLogic.Cutscenes;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.PubSubSystem;
using Kingmaker.UI;
using Kingmaker.UI.Common;
using Kingmaker.UI.Common.Animations;
using Kingmaker.UI.MVVM._PCView.ActionBar;
using Kingmaker.UI.MVVM._PCView.IngameMenu;
using Kingmaker.UI.MVVM._PCView.Other;
using Kingmaker.UI.MVVM._PCView.Party;
using Kingmaker.UI.MVVM._PCView.ServiceWindows.Spellbook.KnownSpells;
using Kingmaker.UI.MVVM._VM.Other;
using Kingmaker.UI.MVVM._VM.Tooltip.Bricks;
using Kingmaker.UI.MVVM._VM.Tooltip.Templates;
using Kingmaker.UI.MVVM._VM.Tooltip.Utils;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.Utility;
using Newtonsoft.Json;
using Owlcat.Runtime.UI.Controls.Button;
using Owlcat.Runtime.UI.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TMPro;
using UniRx;
using UnityEngine;
using UnityEngine.UI;
using BuffIt2TheLimit.Extensions;
using UnityEngine.SceneManagement;
using Kingmaker.UI.SettingsUI;
using Kingmaker.Blueprints.Classes;
using Kingmaker.Localization;
using Kingmaker.Localization.Shared;
using DG.Tweening;
using Kingmaker.Blueprints.Items.Equipment;
using Owlcat.Runtime.UI.Tooltips;
using Kingmaker.UI.Models.Log.CombatLog_ThreadSystem;
using Kingmaker.UI.Models.Log.CombatLog_ThreadSystem.LogThreads.Common;
using Kingmaker.UI.MVVM._PCView.Modificators;
using Kingmaker.Dungeon.Actions;
using Kingmaker.Dungeon;
using Kingmaker.UnitLogic.Parts;

namespace BuffIt2TheLimit {

    struct SpinnerButtons {
        public OwlcatButton up;
        public OwlcatButton down;
    }

    public class BubbleAnimator : MonoBehaviour {
        private static int NextId = 0;

        public Material Target;
        private float Start = UnityEngine.Random.Range(0, 20.0f);
        private float Warmup = 0;
        private double TimeAtDisable;
        private int Id = NextId++;
        private bool WasDisabled = true;

        void Awake() {
            Target.SetFloat("_Warmup", 1);
        }

        void Update() {
            Target.SetFloat("_BubbleTime", Start + Time.unscaledTime);
        }
    }


    public class BubbleBuffSpellbookController : MonoBehaviour {
        private GameObject ToggleButton;
        internal bool IsReady => PartyView != null && MainContainer != null;
        internal bool Buffing => PartyView != null && PartyView.m_Hide;
        private GameObject MainContainer;
        private GameObject NoSpellbooksContainer;
        public RectTransform TooltipRoot;

        private bool WasMainShown = false;

        private PartyPCView PartyView;
        private SavedBufferState save;
        public BufferState state;
        private BuffExecutor Executor;
        private BufferView view;

        private GameObject Root;

        private bool WindowCreated = false;

        private Transform leftPanel;
        private Transform rightPanel;
        private Transform _targetsSection;

        private static readonly string[] SourcePriorityKeys = {
            "priority.spells-scrolls-potions",
            "priority.spells-potions-scrolls",
            "priority.scrolls-spells-potions",
            "priority.scrolls-potions-spells",
            "priority.potions-spells-scrolls",
            "priority.potions-scrolls-spells"
        };

        public static Dictionary<string, TooltipBaseTemplate> AbilityTooltips = new();

        public static TooltipBaseTemplate TooltipForAbility(BlueprintAbility ability) {
            var key = ability.AssetGuid.ToString();
            if (!AbilityTooltips.TryGetValue(key, out var tooltip)) {
                tooltip = new TooltipTemplateAbility(ability);
                AbilityTooltips[key] = tooltip;
            }
            return tooltip;
        }

        public static string SettingsPath => $"{ModSettings.ModEntry.Path}UserSettings/bi2tl-{Game.Instance.Player.GameId}.json";

        public void TryFixEILayout() {
            Transform eiToggle0 = UIHelpers.SpellbookScreen.Find("MainContainer/KnownSpells/ToggleAllSpells");

            if (eiToggle0 != null) {
                Main.Verbose("Tweaking stuff", "interop");
                var eiToggle2 = UIHelpers.SpellbookScreen.Find("MainContainer/KnownSpells/ToggleMetamagic");
                var eiToggle1 = UIHelpers.SpellbookScreen.Find("MainContainer/KnownSpells/TogglePossibleSpells");

                RectTransform[] eiToggles = { (RectTransform)eiToggle0, (RectTransform)eiToggle1, (RectTransform)eiToggle2 };

                for (int i = 0; i < eiToggles.Length; i++) {
                    eiToggles[i].localPosition = new Vector2(430.0f, -392.0f - 30f * i);
                    eiToggles[i].localScale = new Vector3(0.8f, 0.8f, .8f);
                }

                var eiLearnAll = UIHelpers.SpellbookScreen.Find("MainContainer/LearnAllSpells").transform as RectTransform;

                eiLearnAll.localPosition = new Vector2(800.0f, -400.0f);
            }
        }

        public void CreateBuffstate() {
            if (File.Exists(SettingsPath)) {
                try {
                    using var settingsReader = File.OpenText(SettingsPath);
                    using var jsonReader = new JsonTextReader(settingsReader);

                    save = JsonSerializer.CreateDefault().Deserialize<SavedBufferState>(jsonReader);

                    if (save.Version == 0) {
                        MigrateSaveToV1();
                    }
                } catch (JsonException) {
                    //Main.LogError(ex);
                    var messageLog = LogThreadService.Instance.m_Logs[LogChannelType.Common].First(x => x is MessageLogThread);

                    messageLog.AddMessage(new("[Buff It 2 The Limit] Saved buff setup was lost in the 1.4 update, sorry :-(", Color.red, PrefixIcon.None));
                    save = new SavedBufferState();
                }
            } else {
                save = new SavedBufferState();
            }

            state = new(save);
            view = new(state);
            Executor = new(state);

            view.widgetCache = new();
            view.widgetCache.PrefabGenerator = () => {
                SpellbookKnownSpellPCView spellPrefab = null;
                var listPrefab = UIHelpers.SpellbookScreen.Find("MainContainer/KnownSpells");
                var spellsKnownView = listPrefab.GetComponent<SpellbookKnownSpellsPCView>();

                if (spellsKnownView != null)
                    spellPrefab = listPrefab.GetComponent<SpellbookKnownSpellsPCView>().m_KnownSpellView;
                else {
                    foreach (var component in UIHelpers.SpellbookScreen.gameObject.GetComponents<Component>()) {
                        if (component.GetType().FullName == "EnhancedInventory.Controllers.SpellbookController") {
                            Main.Verbose(" ** INSTALLING WORKAROUND FOR ENHANCED INVENTORY **");
                            var fieldHandle = component.GetType().GetField("m_known_spell_prefab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            Main.Verbose($"Got field handle: {fieldHandle != null}");
                            spellPrefab = (SpellbookKnownSpellPCView)fieldHandle.GetValue(component);
                            Main.Verbose($"Found spellPrefab: {spellPrefab != null}");

                            break;
                        }
                    }
                }

                var spellRoot = GameObject.Instantiate(spellPrefab.gameObject);
                spellRoot.name = "BI2TLSpellView";
                spellRoot.DestroyComponents<SpellbookKnownSpellPCView>();
                spellRoot.DestroyChildrenImmediate("Icon/Decoration", "Icon/Domain", "Icon/ForeIcon", "Icon/MythicArtFrame", "Icon/ArtArrowImage", "RemoveButton", "Level");

                return spellRoot;

            };
        }

        private void MigrateSaveToV1() {
            Dictionary<string, string> nameToId = new();
            foreach (var ch in Game.Instance.Player.AllCharacters) {
                nameToId[ch.CharacterName] = ch.UniqueId;
            }

            foreach (SavedBuffState s in save.Buffs.Values) {
                HashSet<string> newWanted = new(s.Wanted.Where(name => nameToId.ContainsKey(name)).Select(name => nameToId[name]));
                s.Wanted = newWanted;

                Dictionary<CasterKey, SavedCasterState> casters = new();
                foreach (var casterEntry in s.Casters) {
                    var key = new CasterKey {
                        Name = nameToId[casterEntry.Key.Name],
                        Spellbook = casterEntry.Key.Spellbook
                    };
                    casters[key] = casterEntry.Value;
                }
                s.Casters = casters;
            }
        }

        private static void FadeOut(GameObject obj) {
            obj.GetComponent<FadeAnimator>().DisappearAnimation();
        }
        private static void FadeIn(GameObject obj) {
            obj.GetComponent<FadeAnimator>().AppearAnimation();
        }

        private List<Material> _ToAnimate = new();

        void Update() {
            foreach (var mat in _ToAnimate)
                mat.SetFloat("_BubbleTime", Time.unscaledTime);
        }

        private void Awake() {
            TryFixEILayout();

            MainContainer = transform.Find("MainContainer").gameObject;
            Main.Verbose($"Found main container: {MainContainer != null}");
            NoSpellbooksContainer = transform.Find("NoSpellbooksContainer").gameObject;
            Main.Verbose($"NospellbooksContainer: {NoSpellbooksContainer != null}");

            PartyView = UIHelpers.StaticRoot.Find("NestedCanvas2/PartyPCView").gameObject.GetComponent<PartyPCView>();

            Main.Verbose($"PartyView: {PartyView != null}");

            GameObject.Destroy(transform.Find("bi2tl-toggle")?.gameObject);
            GameObject.Destroy(transform.Find("bi2tl-root")?.gameObject);

            var parent = GameObject.Instantiate(transform.Find("MainContainer/MetamagicButton"), transform);
            (parent.transform as RectTransform).anchoredPosition = new Vector2(1400, 0);
            GameObject.Destroy(parent.transform.Find("FrameImage/MagicHackButton").gameObject);
            
            ToggleButton = parent.transform.Find("FrameImage/MetamagicButton").gameObject;
            ToggleButton.name = "bi2tl-toggle";
            ToggleButton.GetComponentInChildren<TextMeshProUGUI>().text = "buffsetup".i8();

            {
                var button = ToggleButton.GetComponentInChildren<OwlcatButton>();
                button.OnLeftClick.RemoveAllListeners();
                button.OnLeftClick.AddListener(() => {
                    ToggleBuffMode();
                });
            }

            Root = new GameObject("bi2tl-root", typeof(RectTransform));
            Root.SetActive(false);
            Root.transform.SetParent(transform);
            var rect = Root.transform as RectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.localPosition = Vector3.zero;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            var group = Root.AddComponent<CanvasGroup>();
            var fader = Root.AddComponent<FadeAnimator>();

            // Ensure toggle button renders above the buff window frame
            parent.transform.SetAsLastSibling();
        }

        internal void Hide() {
            if (Root != null) {
                FadeOut(Root);
                Root.SetActive(false);
            }
        }

        internal void EnsurePartyViewHidden() {
            if (PartyView != null && !PartyView.m_Hide) {
                PartyView.HideAnimation(true);
            }
        }

        public void ToggleBuffMode() {
            if (PartyView == null) {
                // Re-acquire PartyView if it was lost (e.g. UI hierarchy was rebuilt)
                var partyViewGO = UIHelpers.StaticRoot.Find("NestedCanvas2/PartyPCView");
                if (partyViewGO != null)
                    PartyView = partyViewGO.gameObject.GetComponent<PartyPCView>();
                if (PartyView == null) {
                    Main.Log("BuffIt2TheLimit: PartyView is null, cannot toggle buff mode");
                    return;
                }
            }
            PartyView.HideAnimation(!Buffing);

            if (Buffing) {
                WasMainShown = MainContainer.activeSelf;
                if (WasMainShown)
                    FadeOut(MainContainer);
                else
                    FadeOut(NoSpellbooksContainer);
                MainContainer.SetActive(false);
                NoSpellbooksContainer.SetActive(false);
                ShowBuffWindow();
                ToggleButton.GetComponentInChildren<TextMeshProUGUI>().text = "buffexit".i8();
            } else {
                Hide();
                if (WasMainShown) {
                    FadeIn(MainContainer);
                    MainContainer.SetActive(true);
                } else {
                    FadeIn(NoSpellbooksContainer);
                    NoSpellbooksContainer.SetActive(true);
                }
                ToggleButton.GetComponentInChildren<TextMeshProUGUI>().text = "buffsetup".i8();
            }
        }

        private static GameObject MakeToggle(GameObject togglePrefab, Transform parent, float x, float y, string text, string name, float scale = 1) {
            var toggle = GameObject.Instantiate(togglePrefab, parent);
            toggle.name = name;
            var toggleRect = toggle.transform as RectTransform;
            toggleRect.localPosition = Vector2.zero;
            toggleRect.anchoredPosition = Vector2.zero;
            toggleRect.anchorMin = new Vector2(x, y);
            toggleRect.anchorMax = new Vector2(x, y);
            toggleRect.pivot = new Vector2(0.5f, 0.5f);
            toggleRect.localScale = new Vector3(scale, scale, scale);
            toggle.GetComponentInChildren<TextMeshProUGUI>().text = text;
            toggle.SetActive(true);
            return toggle;
        }

        private Portrait CreatePortrait(float groupHeight, Transform groupRect, bool createLabel, bool createPopout, Portrait[] group = null, GameObject popout = null) {
            var portrait = new Portrait();
            float width = groupHeight * .75f;
            float height = groupHeight;

            var (p, pRect) = UIHelpers.Create($"bi2tl-portrait", groupRect);

            portrait.GameObject = p;
            portrait.Image = p.AddComponent<Image>();
            p.MakeComponent<LayoutElement>(l => {
                l.preferredWidth = width;
                l.preferredHeight = height;
            });

            var normalBorder = AssetLoader.Sprites["UI_HudCharacterFrameBorder_Default"];
            var hoverBorder = AssetLoader.Sprites["UI_HudCharacterFrameBorder_Hover"];

            var (fullOverlay, fullOverlayRect) = UIHelpers.Create("full-overlay", pRect);
            fullOverlayRect.FillParent();
            portrait.FullOverlay = fullOverlay.MakeComponent<Image>(img => {
                img.material = AssetLoader.Materials["bubble_overlay_full"];
                img.gameObject.SetActive(false);
                img.color = new Color(1, 1, 1, UnityEngine.Random.Range(0f, 1.0f));
            });

            var (aoeOverlay, aoeOverlayRect) = UIHelpers.Create("aoe-overlay", pRect);
            aoeOverlayRect.FillParent();
            //aoeOverlayRect.anchorMax = new Vector2(1, 0.4f);
            portrait.Overlay = aoeOverlay.MakeComponent<Image>(img => {
                img.material = AssetLoader.Materials["bubbly_overlay"];
                img.gameObject.SetActive(false);
                img.color = new Color(0, 1, 0, UnityEngine.Random.Range(0f, 1.0f));
            });

            var (frameObj, _) = UIHelpers.Create("child-image", pRect);
            var frame = frameObj.AddComponent<Image>();
            frame.type = Image.Type.Sliced;
            frameObj.FillParent();
            frame.sprite = normalBorder;

            var (sourceOverlayBgObj, sourceOverlayBgRect) = UIHelpers.Create("source-overlay-bg", pRect);
            sourceOverlayBgRect.anchorMin = new Vector2(0.4f, 0.0f);
            sourceOverlayBgRect.anchorMax = new Vector2(1.0f, 0.55f);
            sourceOverlayBgRect.offsetMin = Vector2.zero;
            sourceOverlayBgRect.offsetMax = Vector2.zero;
            portrait.SourceOverlayBg = sourceOverlayBgObj.AddComponent<Image>();
            portrait.SourceOverlayBg.color = new Color(0, 0, 0, 0.6f);
            sourceOverlayBgObj.SetActive(false);

            var (sourceOverlayObj, sourceOverlayRect) = UIHelpers.Create("source-overlay", pRect);
            sourceOverlayRect.anchorMin = new Vector2(0.4f, 0.0f);
            sourceOverlayRect.anchorMax = new Vector2(1.0f, 0.55f);
            sourceOverlayRect.offsetMin = Vector2.zero;
            sourceOverlayRect.offsetMax = Vector2.zero;
            portrait.SourceOverlay = sourceOverlayObj.AddComponent<Image>();
            portrait.SourceOverlay.preserveAspect = true;
            sourceOverlayObj.SetActive(false);

            portrait.Button = p.AddComponent<OwlcatButton>();
            portrait.Button.OnHover.AddListener(h => {
                frame.sprite = h ? hoverBorder : normalBorder;
            });


            if (createLabel) {
                var labelPrefab = UIHelpers.SpellbookScreen.Find("MainContainer/MemorizingPanelContainer/MemorizingPanel/SubstituteContainer/Label").gameObject;
                var label = GameObject.Instantiate(labelPrefab, pRect);
                label.Rect().SetAnchor(0.5, 0.5, -.25, -.25);
                label.Rect().sizeDelta = new Vector2(width, 1);
                label.SetActive(true);
                portrait.Text = label.GetComponentInChildren<TextMeshProUGUI>();
                portrait.Text.richText = true;
                portrait.Text.lineSpacing = -15.0f;
                portrait.Text.text = "HELLO";
            }

            if (createPopout) {
                var expand = GameObject.Instantiate(expandButtonPrefab, pRect);
                expand.Rect().pivot = new Vector2(0.5f, 0.5f);
                expand.Rect().SetAnchor(0.5, 1);
                expand.GetComponent<OwlcatButton>().Interactable = true;
                expand.SetActive(true);
                portrait.Expand = expand.GetComponent<OwlcatButton>();
                var fader = popout.GetComponent<FadeAnimator>();
                portrait.Expand.OnLeftClick.AddListener(() => {
                    Main.Safely(() => {
                        portrait.SetExpanded(!portrait.State);
                        if (portrait.State) {
                            foreach (var p in group)
                                if (p != portrait)
                                    p.SetExpanded(false);

                            popout.transform.SetParent(pRect);
                            popout.Rect().anchoredPosition = new Vector2(0, 18);
                            popout.Rect().pivot = new Vector2(0.5f, 0);
                            popout.Rect().SetAnchor(0.5, 1);
                            if (fader != null) {
                                fader.AppearAnimation();
                            }
                            popout.SetActive(true);
                        } else {
                            if (fader != null) {
                                fader.DisappearAnimation();
                            }
                        }
                    });
                });
                portrait.SetExpanded(false);
            }

            return portrait;
        }

        public ReactiveProperty<bool> SortByName = new(false);
        public ReactiveProperty<bool> ShowNotRequested = new(true);
        public ReactiveProperty<bool> ShowRequested = new(true);
        public ReactiveProperty<bool> ShowShort = new(true);
        public ReactiveProperty<bool> ShowHidden = new(false);
        public ReactiveProperty<string> NameFilter = new("");
        public ButtonGroup<Category> CurrentCategory;

        public GameObject expandButtonPrefab;

        private void CreateWindow() {
            var staticRoot = UIHelpers.StaticRoot;


            var portraitPrefab = staticRoot.Find("NestedCanvas2/PartyPCView/Viewport/Content/PartyCharacterView_01").gameObject;
            Main.Verbose("Got portrait prefab");
            var listPrefab = UIHelpers.SpellbookScreen.Find("MainContainer/KnownSpells");
            Main.Verbose("Got list prefab");
            var spellsKnownView = listPrefab.GetComponent<SpellbookKnownSpellsPCView>();

            Main.Verbose("Got spell prefab");
            var framePrefab = UIHelpers.MythicInfoView.Find("Window/MainContainer/MythicInfoProgressionView/Progression/Frame").gameObject;
            Main.Verbose("Got frame prefab");
            expandButtonPrefab = UIHelpers.EncyclopediaView.Find("EncyclopediaPageView/HistoryManagerGroup/HistoryGroup/PreviousButton").gameObject;
            Main.Verbose("Got expandButton prefab");
            var toggleTransform = UIHelpers.SpellbookScreen.Find("MainContainer/KnownSpells/Toggle");
            if (toggleTransform == null)
                toggleTransform = UIHelpers.SpellbookScreen.Find("MainContainer/KnownSpells/TogglePossibleSpells");


            var togglePrefab = toggleTransform.gameObject;
            Main.Verbose("Got toggle prefab: ");
            buttonPrefab = UIHelpers.SpellbookScreen.Find("MainContainer/MetamagicContainer/Metamagic/Button").gameObject;
            Main.Verbose("Got button prefab: ");
            selectedPrefab = UIHelpers.CharacterScreen.Find("Menu/Button/Selected").gameObject;
            Main.Verbose("Got selected prefab");
            var nextPrefab = staticRoot.Find("NestedCanvas2/PartyPCView/Background/Next").gameObject;
            Main.Verbose("Got next prefab");
            var prevPrefab = staticRoot.Find("NestedCanvas2/PartyPCView/Background/Prev").gameObject;
            Main.Verbose("Got prev prefab");

            var content = Root.transform;
            Main.Verbose("got root.transform");

            view.listPrefab = listPrefab.gameObject;
            view.content = content;
            Main.Verbose("set view prefabs");

            // Create left/right panel containers for vertical split layout
            var (leftPanelObj, leftPanelRect) = UIHelpers.Create("left-panel", content);
            leftPanelRect.anchorMin = new Vector2(0.05f, 0.05f);
            leftPanelRect.anchorMax = new Vector2(0.38f, 0.89f);
            leftPanelRect.offsetMin = Vector2.zero;
            leftPanelRect.offsetMax = Vector2.zero;
            leftPanel = leftPanelObj.transform;

            var (rightPanelObj, rightPanelRect) = UIHelpers.Create("right-panel", content);
            rightPanelRect.anchorMin = new Vector2(0.40f, 0.05f);
            rightPanelRect.anchorMax = new Vector2(0.95f, 0.89f);
            rightPanelRect.offsetMin = Vector2.zero;
            rightPanelRect.offsetMax = Vector2.zero;
            rightPanel = rightPanelObj.transform;

            view.leftPanel = leftPanel;

            view.MakeSummary();

            view.MakeBuffsList();

            MakeFilters(togglePrefab, leftPanel);

            // Calculate totalCasters upfront (needed by MakeDetailsView)
            totalCasters = 0;
            for (int i = 0; i < Bubble.Group.Count; i++) {
                totalCasters += Bubble.Group[i].Spellbooks?.Count() ?? 0;
            }

            MakeDetailsView(portraitPrefab, framePrefab, nextPrefab, prevPrefab, togglePrefab, expandButtonPrefab, rightPanel);
            Main.Verbose("made details view");

            MakeGroupHolder(portraitPrefab, expandButtonPrefab, buttonPrefab, _targetsSection);
            Main.Verbose("made group holder");

            var partialOverlay = AssetLoader.Materials["bubbly_overlay"];
            partialOverlay.SetFloat("_Speed", 0.3f);
            partialOverlay.SetFloat("_Warmup", 1);
            _ToAnimate.Add(partialOverlay);
            var fullOverlay = AssetLoader.Materials["bubble_overlay_full"];
            fullOverlay.SetFloat("_Speed", 0.3f);
            fullOverlay.SetFloat("_Warmup", 1);
            _ToAnimate.Add(fullOverlay);

            MakeSettings(togglePrefab, content);

            var tooltipRootObj = new GameObject("tooltip-root", typeof(RectTransform));
            TooltipRoot = tooltipRootObj.Rect();
            TooltipRoot.AddTo(Root);
            TooltipRoot.SetAsLastSibling();



            ShowHidden.Subscribe<bool>(show => {
                RefreshFiltering();
            });
            ShowNotRequested.Subscribe<bool>(show => {
                RefreshFiltering();
            });
            ShowRequested.Subscribe<bool>(show => {
                RefreshFiltering();
            });
            ShowShort.Subscribe<bool>(show => {
                RefreshFiltering();
            });
            NameFilter.Subscribe<string>(val => {
                if (search.InputField.text != val)
                    search.InputField.text = val;
                RefreshFiltering();
            });
            SortByName.Subscribe<bool>(show => {
                RefreshFiltering();
            });



            view.currentSelectedSpell.Subscribe(val => {
                try {
                    HideCasterPopout?.Invoke();
                    if (view.currentSelectedSpell.HasValue && view.currentSelectedSpell.Value != null) {
                        var buff = view.Selected;

                        if (buff == null) {
                            currentSpellView.SetActive(false);
                            return;
                        }

                        currentSpellView.SetActive(true);
                        BubbleSpellView.BindBuffToView(buff, currentSpellView);

                        view.addToAll.SetActive(true);
                        view.removeFromAll.SetActive(true);

                        //float actualWidth = (buff.CasterQueue.Count - 1) * castersHolder.GetComponent<HorizontalLayoutGroup>().spacing;
                        //(castersHolder.transform as RectTransform).anchoredPosition = new Vector2(-actualWidth / 2.0f, 0);
                        view.Update();
                    } else {
                        currentSpellView.SetActive(false);

                        view.addToAll.SetActive(false);
                        view.removeFromAll.SetActive(false);

                        foreach (var caster in view.casterPortraits)
                            caster.GameObject.SetActive(false);

                        foreach (var portrait in view.targets) {
                            portrait.FullOverlay.gameObject.SetActive(false);
                            portrait.Button.Interactable = true;
                        }
                    }
                } catch (Exception e) {
                    Main.Error(e, "SELECTING SPELL");
                }
            });
            view.OnUpdate = () => {
                UpdateDetailsView?.Invoke();
            };
            WindowCreated = true;
        }

        private void MakeKeybindRow(Transform parent, string labelText, Func<ShortcutBinding> getter, Action<ShortcutBinding> setter) {
            var row = new GameObject($"keybind-row", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var hg = row.AddComponent<HorizontalLayoutGroup>();
            hg.childControlHeight = true;
            hg.childControlWidth = true;
            hg.childForceExpandWidth = false;
            hg.spacing = 8;

            var labelObj = new GameObject("label", typeof(RectTransform));
            labelObj.transform.SetParent(row.transform, false);
            var labelLE = labelObj.AddComponent<LayoutElement>();
            labelLE.flexibleWidth = 1;
            var lText = labelObj.AddComponent<TextMeshProUGUI>();
            lText.text = labelText;
            lText.fontSize = 14;
            lText.color = new Color(0.2f, 0.2f, 0.2f);
            lText.alignment = TextAlignmentOptions.MidlineLeft;

            var btnObj = UnityEngine.Object.Instantiate(buttonPrefab, row.transform);
            var btnLE = btnObj.GetComponent<LayoutElement>() ?? btnObj.AddComponent<LayoutElement>();
            btnLE.preferredWidth = 120;
            btnLE.flexibleWidth = 0;
            var btnText = btnObj.GetComponentInChildren<TextMeshProUGUI>();
            btnText.text = getter().ToDisplayString();

            var btn = btnObj.GetComponent<OwlcatButton>();
            btn.OnLeftClick.AddListener(() => {
                if (BubbleBuffGlobalController.CapturingActive) return;
                btnText.text = "shortcut.press".i8();
                BubbleBuffGlobalController.CapturingActive = true;
                BubbleBuffGlobalController.OnShortcutCaptured = (binding) => {
                    setter(binding);
                    btnText.text = binding.ToDisplayString();
                };
            });
        }

        private static (ToggleWorkaround, TextMeshProUGUI) MakeSettingsToggle(GameObject prefab, Transform content, string text) {
            var toggleObj = GameObject.Instantiate(prefab, content);
            toggleObj.SetActive(true);
            toggleObj.Rect().localPosition = Vector3.zero;
            toggleObj.GetComponent<HorizontalLayoutGroup>().childControlWidth = true;
            var label = toggleObj.GetComponentInChildren<TextMeshProUGUI>();
            label.text = text;
            return (toggleObj.GetComponentInChildren<ToggleWorkaround>(), label);
        }

        private void MakeSettings(GameObject togglePrefab, Transform content) {
            Main.VerboseNotNull(() => togglePrefab);
            Main.VerboseNotNull(() => content);
            var staticRoot = Game.Instance.UI.Canvas.transform;
            var button = staticRoot.Find("NestedCanvas1/IngameMenuView/ButtonsPart/Container/SettingsButton").gameObject;
            Main.VerboseNotNull(() => button);

            var toggleSettings = GameObject.Instantiate(button, content);
            toggleSettings.Rect().anchoredPosition = Vector3.zero;
            toggleSettings.Rect().pivot = new Vector2(1, 0);
            toggleSettings.Rect().SetAnchor(.93, .10);

            var actionBarView = UIHelpers.StaticRoot.Find("NestedCanvas1/ActionBarPcView").GetComponent<ActionBarPCView>();
            var panel = GameObject.Instantiate(actionBarView.m_DragSlot.m_ConvertedView.gameObject, toggleSettings.transform);
            panel.DestroyComponents<ActionBarConvertedPCView>();
            panel.DestroyComponents<GridLayoutGroup>();
            panel.SetActive(false);
            panel.Rect().SetAnchor(0, 1);
            panel.Rect().sizeDelta = new Vector2(100, 200);
            panel.Rect().pivot = new Vector2(1, 0);
            panel.Rect().anchoredPosition = Vector3.zero;
            panel.Rect().anchoredPosition = new Vector2(-3, 3);

            var popGrid = panel.AddComponent<GridLayoutGroup>();
            popGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            popGrid.constraintCount = 1;
            int width = 450;
            if (Language.Locale == Locale.deDE)
                width = 550;
            popGrid.cellSize = new Vector2(width, 40);
            popGrid.padding.left = 25;
            popGrid.padding.top = 12;
            popGrid.padding.bottom = 50;
            popGrid.startCorner = GridLayoutGroup.Corner.LowerLeft;

            {
                var (toggle, _) = MakeSettingsToggle(togglePrefab, panel.transform, "setting-in-combat".i8());
                toggle.isOn = state.AllowInCombat;
                toggle.onValueChanged.AddListener(enabled => {
                    state.AllowInCombat = enabled;
                    bool allow = !Game.Instance.Player.IsInCombat || enabled;
                    GlobalBubbleBuffer.Instance.Buttons.ForEach(b => b.Interactable = allow);
                });
            }

            {
                var (toggle, _) = MakeSettingsToggle(togglePrefab, panel.transform, "setting-overwritebuff".i8());
                toggle.isOn = state.OverwriteBuff;
                toggle.onValueChanged.AddListener(enabled => {
                    state.OverwriteBuff = enabled;
                });
            }

            {
                var (toggle, _) = MakeSettingsToggle(togglePrefab, panel.transform, "setting-verbose".i8());
                toggle.isOn = state.VerboseCasting;
                toggle.onValueChanged.AddListener(enabled => {
                    state.VerboseCasting = enabled;
                });
            }

            // === Scroll/Potion Settings ===

            {
                var (toggle, _) = MakeSettingsToggle(togglePrefab, panel.transform, "setting-scrolls-enabled".i8());
                toggle.isOn = state.SavedState.ScrollsEnabled;
                toggle.onValueChanged.AddListener(enabled => {
                    state.SavedState.ScrollsEnabled = enabled;
                    state.InputDirty = true;
                    state.Save(true);
                });
            }

            {
                var (toggle, _) = MakeSettingsToggle(togglePrefab, panel.transform, "setting-potions-enabled".i8());
                toggle.isOn = state.SavedState.PotionsEnabled;
                toggle.onValueChanged.AddListener(enabled => {
                    state.SavedState.PotionsEnabled = enabled;
                    state.InputDirty = true;
                    state.Save(true);
                });
            }

            {
                var (toggle, _) = MakeSettingsToggle(togglePrefab, panel.transform, "setting-equipment-enabled".i8());
                toggle.isOn = state.SavedState.EquipmentEnabled;
                toggle.onValueChanged.AddListener(enabled => {
                    state.SavedState.EquipmentEnabled = enabled;
                    state.InputDirty = true;
                    state.Save(true);
                });
            }

            {
                var (toggle, _) = MakeSettingsToggle(togglePrefab, panel.transform, "setting-songs-enabled".i8());
                toggle.isOn = state.SavedState.SongsEnabled;
                toggle.onValueChanged.AddListener(enabled => {
                    state.SavedState.SongsEnabled = enabled;
                    state.InputDirty = true;
                    state.Save(true);
                });
            }

            // UMD Retries (label + buttons)
            {
                var labelObj = GameObject.Instantiate(togglePrefab, panel.transform);
                labelObj.DestroyComponents<ToggleWorkaround>();
                labelObj.DestroyChildren("Background");
                labelObj.SetActive(true);
                var label = labelObj.GetComponentInChildren<TextMeshProUGUI>();
                label.text = $"{"setting-umd-retries".i8()}: {state.SavedState.UmdRetries}";

                var buttonHolder = new GameObject("umd-retries-buttons", typeof(RectTransform));
                buttonHolder.transform.SetParent(panel.transform);
                var hlg = buttonHolder.AddComponent<HorizontalLayoutGroup>();
                hlg.childForceExpandWidth = false;
                hlg.spacing = 5;

                var downButton = MakeButton("-", buttonHolder.transform);
                downButton.GetComponentInChildren<OwlcatButton>().OnLeftClick.AddListener(() => {
                    if (state.SavedState.UmdRetries > 1) {
                        state.SavedState.UmdRetries--;
                        label.text = $"{"setting-umd-retries".i8()}: {state.SavedState.UmdRetries}";
                        state.Save(true);
                    }
                });

                var upButton = MakeButton("+", buttonHolder.transform);
                upButton.GetComponentInChildren<OwlcatButton>().OnLeftClick.AddListener(() => {
                    if (state.SavedState.UmdRetries < 20) {
                        state.SavedState.UmdRetries++;
                        label.text = $"{"setting-umd-retries".i8()}: {state.SavedState.UmdRetries}";
                        state.Save(true);
                    }
                });
            }

            // UMD Mode cycle button
            {
                string GetUmdModeText() => state.SavedState.UmdMode switch {
                    UmdMode.SafeOnly => "umd.safeonly".i8(),
                    UmdMode.AllowIfPossible => "umd.allowifpossible".i8(),
                    UmdMode.AlwaysTry => "umd.alwaystry".i8(),
                    _ => "?"
                };

                var labelObj = GameObject.Instantiate(togglePrefab, panel.transform);
                labelObj.DestroyComponents<ToggleWorkaround>();
                labelObj.DestroyChildren("Background");
                labelObj.SetActive(true);
                var umdText = labelObj.GetComponentInChildren<TextMeshProUGUI>();
                umdText.text = $"{"setting-umd-mode".i8()}: {GetUmdModeText()}";

                var cycleButton = MakeButton(">", panel.transform);
                cycleButton.GetComponentInChildren<OwlcatButton>().OnLeftClick.AddListener(() => {
                    state.SavedState.UmdMode = (UmdMode)(((int)state.SavedState.UmdMode + 1) % 3);
                    umdText.text = $"{"setting-umd-mode".i8()}: {GetUmdModeText()}";
                    state.InputDirty = true;
                    state.Save(true);
                });
            }

            // Source Priority cycle button
            {
                var labelObj = GameObject.Instantiate(togglePrefab, panel.transform);
                labelObj.DestroyComponents<ToggleWorkaround>();
                labelObj.DestroyChildren("Background");
                labelObj.SetActive(true);
                var prioText = labelObj.GetComponentInChildren<TextMeshProUGUI>();
                prioText.text = $"{"setting-source-priority".i8()}: {SourcePriorityKeys[(int)state.SavedState.GlobalSourcePriority].i8()}";

                var prioCycleButton = MakeButton(">", panel.transform);
                prioCycleButton.GetComponentInChildren<OwlcatButton>().OnLeftClick.AddListener(() => {
                    state.SavedState.GlobalSourcePriority = (SourcePriority)(((int)state.SavedState.GlobalSourcePriority + 1) % 6);
                    prioText.text = $"{"setting-source-priority".i8()}: {SourcePriorityKeys[(int)state.SavedState.GlobalSourcePriority].i8()}";
                    state.InputDirty = true;
                    state.Save(true);
                });
            }

            // Keyboard shortcut per group
            foreach (BuffGroup group in Enum.GetValues(typeof(BuffGroup))) {
                var groupCopy = group;
                var key = $"shortcut.{group.ToString().ToLower()}";
                MakeKeybindRow(panel.transform, key.i8(),
                    () => state.GetShortcut(groupCopy),
                    binding => state.SetShortcut(groupCopy, binding));
            }

            // Open buff menu shortcut
            MakeKeybindRow(panel.transform, "shortcut.openbuffmenu".i8(),
                () => state.GetOpenBuffMenuShortcut(),
                binding => state.SetOpenBuffMenuShortcut(binding));

            var b = toggleSettings.GetComponent<OwlcatButton>();
            b.SetTooltip(new TooltipTemplateSimple("settings".i8(), "settings-toggle".i8()), new TooltipConfig {
                InfoCallPCMethod = InfoCallPCMethod.None,
            });

            b.OnLeftClick.AddListener(() => {
                panel.SetActive(!panel.activeSelf);
                b.IsPressed = panel.activeSelf;
            });
        }

        private void RegenerateWidgetCache(Transform listPrefab, SpellbookKnownSpellsPCView spellsKnownView) {
            if (view.widgetCache == null) {
            }
        }

        public static GameObject buttonPrefab;
        public static GameObject selectedPrefab;


        public static GameObject MakeButton(string title, Transform parent) {
            var button = GameObject.Instantiate(buttonPrefab, parent);
            button.GetComponentInChildren<TextMeshProUGUI>().text = title;
            var buttonRect = button.transform as RectTransform;
            buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
            buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRect.pivot = new Vector2(1, 1);
            buttonRect.localPosition = Vector3.zero;
            buttonRect.anchoredPosition = Vector2.zero;
            return button;
        }

        public class ButtonGroup<T> {
            public ReactiveProperty<T> Selected = new();
            private readonly Transform content;

            public ButtonGroup(Transform content) {
                this.content = content;
            }

            public T Value {
                get => Selected.Value;
                set => Selected.Value = value;
            }

            public void Add(T value, string title) {
                var button = MakeButton(title, content);

                var selection = GameObject.Instantiate(selectedPrefab, button.transform);
                selection.SetActive(false);

                Selected.Subscribe<T>(s => {
                    selection.SetActive(EqualityComparer<T>.Default.Equals(s, value));
                });
                button.GetComponentInChildren<OwlcatButton>().Interactable = true;
                button.GetComponentInChildren<OwlcatButton>().OnLeftClick.AddListener(() => {
                    Selected.Value = value;
                });
            }

            public void Add(T value, string title, Sprite icon) {
                var button = MakeButton(title, content);

                if (icon != null) {
                    var iconObj = new GameObject("tab-icon", typeof(RectTransform));
                    iconObj.transform.SetParent(button.transform, false);
                    iconObj.transform.SetAsFirstSibling();
                    var img = iconObj.AddComponent<Image>();
                    img.sprite = icon;
                    img.preserveAspect = true;
                    var le = iconObj.AddComponent<LayoutElement>();
                    le.preferredWidth = 24;
                    le.preferredHeight = 24;
                }

                var selection = GameObject.Instantiate(selectedPrefab, button.transform);
                selection.SetActive(false);

                Selected.Subscribe<T>(s => {
                    selection.SetActive(EqualityComparer<T>.Default.Equals(s, value));
                });
                button.GetComponentInChildren<OwlcatButton>().Interactable = true;
                button.GetComponentInChildren<OwlcatButton>().OnLeftClick.AddListener(() => {
                    Selected.Value = value;
                });
            }

        }

        public static RectTransform MakeVerticalRect(string name, Transform parent) {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.AddComponent<VerticalLayoutGroup>().childForceExpandHeight = false;
            var rect = obj.Rect();
            rect.SetParent(parent, false);
            rect.localPosition = Vector3.zero;
            rect.localScale = Vector3.one;
            rect.anchoredPosition = Vector2.zero;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        private void MakeFilters(GameObject togglePrefab, Transform content) {
            var filterRect = MakeVerticalRect("filters", content);
            //filterToggles.AddComponent<Image>().color = Color.green;
            filterRect.anchorMin = new Vector2(0f, 0.0f);
            filterRect.anchorMax = new Vector2(0.55f, 0.30f);
            filterRect.gameObject.EditComponent<VerticalLayoutGroup>(v => {
                v.childScaleHeight = true;
                v.childScaleWidth = true;
                //v.childForceExpandWidth = true;
                v.childControlWidth = false;
            });

            search = new SearchBar(filterRect, "...", false, "bi2tl-search-buff");
            var searchRect = search.RootGameObject.transform as RectTransform;
            searchRect.sizeDelta = new Vector2(280, 50);

            const float scale = 0.8f;
            GameObject showHidden = MakeToggle(togglePrefab, filterRect, 0.8f, .5f, "showhidden".i8(), "bi2tl-toggle-show-hidden", scale);
            GameObject showShort = MakeToggle(togglePrefab, filterRect, .8f, .5f, "showshort".i8(), "bi2tl-toggle-show-short", scale);
            GameObject showRequested = MakeToggle(togglePrefab, filterRect, .8f, .5f, "showreq".i8(), "bi2tl-toggle-show-requested", scale);
            GameObject showNotRequested = MakeToggle(togglePrefab, filterRect, .8f, .5f, "showNOTreq".i8(), "bi2tl-toggle-show-not-requested", scale);
            GameObject sortByName = MakeToggle(togglePrefab, filterRect, .8f, .5f, "sort.name".i8(), "bi2tl-toggle-sort-by-name", scale);

            search.InputField.onValueChanged.AddListener(val => {
                NameFilter.Value = val;
            });

            var categoryRect = MakeVerticalRect("categories", content);
            categoryRect.anchorMin = new Vector2(0.57f, 0.0f);
            categoryRect.anchorMax = new Vector2(1f, 0.30f);

            CurrentCategory = new ButtonGroup<Category>(categoryRect);
            CurrentCategory.Selected.Subscribe<Category>(_ => RefreshFiltering());

            CurrentCategory.Add(Category.Buff, "cat.buffs".i8(), GlobalBubbleBuffer.tabBuffsIcon);
            CurrentCategory.Add(Category.Ability, "cat.Abilities".i8(), GlobalBubbleBuffer.tabAbilitiesIcon);
            CurrentCategory.Add(Category.Equipment, "cat.Equipment".i8(), GlobalBubbleBuffer.tabEquipmentIcon);
            CurrentCategory.Add(Category.Song, "cat.Songs".i8(), GlobalBubbleBuffer.tabSongsIcon);


            ShowShort.BindToView(showShort);
            ShowHidden.BindToView(showHidden);
            ShowRequested.BindToView(showRequested);
            ShowNotRequested.BindToView(showNotRequested);
            SortByName.BindToView(sortByName);

            CurrentCategory.Selected.Value = Category.Buff;
        }

        private void RefreshFiltering() {
            if (state.BuffList == null)
                return;

            if (SortByName.Value) {
                view.DisplayOrder.Sort((a, b) => {
                    if (a.name == b.name) {
                        if (a.key.MetamagicMask == 0 && b.key.MetamagicMask > 0) {
                            return -1;
                        } else if (a.key.MetamagicMask > 0 && b.key.MetamagicMask == 0) {
                            return 1;
                        } else {
                            return a.key.Archmage ? 1 : -1;
                        }
                    } else {
                        return a.name.CompareTo(b.name);
                    }
                });
            } else {
                view.DisplayOrder.Sort((a, b) => {
                    return a.discovery - b.discovery;
                });
            }

            foreach (var k in view.DisplayOrder) {
                view.buffWidgets[k.key].transform.SetAsLastSibling();
            }

            foreach (var buff in state.BuffList) {

                if (!view.buffWidgets.TryGetValue(buff.Key, out var widget) || widget == null)
                    continue;

                bool show = true;

                if (buff.Category != CurrentCategory.Value)
                    show = false;

                bool showForRequested = ShowRequested.Value && buff.Requested > 0 || ShowNotRequested.Value && buff.Requested == 0;
                if (!showForRequested)
                    show = false;

                if (NameFilter.Value.Length > 0) {
                    var filterString = NameFilter.Value.ToLower();
                    if (!buff.NameLower.Contains(filterString))
                        show = false;
                }

                if (!ShowHidden.Value && buff.HideBecause(HideReason.Blacklisted))
                    show = false;
                if (!ShowShort.value && buff.HideBecause(HideReason.Short))
                    show = false;

                widget.SetActive(show);
            }
        }

        private Action HideCasterPopout;
        private Action UpdateDetailsView;

        private static BlueprintFeature PowerfulChangeFeature => Resources.GetBlueprint<BlueprintFeature>("5e01e267021bffe4e99ebee3fdc872d1");
        private static BlueprintFeature ShareTransmutationFeature => Resources.GetBlueprint<BlueprintFeature>("c4ed8d1a90c93754eacea361653a7d56");
        private static BlueprintFeature AzataZippyMagicFeature => Resources.GetBlueprint<BlueprintFeature>("30b4200f897ba25419ba3a292aed4053");

        private void MakeDetailsView(GameObject portraitPrefab,
                                     GameObject framePrefab,
                                     GameObject nextPrefab,
                                     GameObject prevPrefab,
                                     GameObject togglePrefab,
                                     GameObject expandButtonPrefab,
                                     Transform content) {

            Main.VerboseNotNull(() => portraitPrefab);
            Main.VerboseNotNull(() => framePrefab);
            Main.VerboseNotNull(() => nextPrefab);
            Main.VerboseNotNull(() => prevPrefab);
            Main.VerboseNotNull(() => togglePrefab);
            Main.VerboseNotNull(() => expandButtonPrefab);
            Main.VerboseNotNull(() => content);

            var detailsHolder = GameObject.Instantiate(framePrefab, content);
            var detailsRect = detailsHolder.GetComponent<RectTransform>();
            GameObject.Destroy(detailsHolder.transform.Find("FrameDecor").gameObject);
            Main.Verbose("destroyed FrameDecor");

            detailsRect.localPosition = Vector2.zero;
            detailsRect.sizeDelta = Vector2.zero;
            detailsRect.anchorMin = new Vector2(0f, 0f);
            detailsRect.anchorMax = new Vector2(1f, 1f);

            // Disable raycast on frame background so target portraits underneath can receive clicks
            var detailsBgImage = detailsHolder.GetComponent<Image>();
            if (detailsBgImage != null)
                detailsBgImage.raycastTarget = false;

            // VLG flow container for automatic vertical stacking
            var flowObj = new GameObject("details-flow", typeof(RectTransform));
            var flowRect = flowObj.GetComponent<RectTransform>();
            flowRect.SetParent(detailsRect, false);
            flowRect.anchorMin = Vector2.zero;
            flowRect.anchorMax = Vector2.one;
            flowRect.offsetMin = Vector2.zero;
            flowRect.offsetMax = Vector2.zero;
            var flowVLG = flowObj.AddComponent<VerticalLayoutGroup>();
            flowVLG.childForceExpandHeight = false;
            flowVLG.childForceExpandWidth = true;
            flowVLG.childControlHeight = true;
            flowVLG.childControlWidth = true;
            flowVLG.spacing = 2;
            flowVLG.padding = new RectOffset(8, 8, 20, 8);

            // Section containers — proportional heights via flex weights
            GameObject MakeSection(string name, float flexWeight, float minH = 30f) {
                var sectionObj = new GameObject(name, typeof(RectTransform));
                var sectionRect = sectionObj.GetComponent<RectTransform>();
                sectionRect.SetParent(flowObj.transform, false);
                var le = sectionObj.AddComponent<LayoutElement>();
                le.minHeight = minH;
                le.preferredHeight = minH;
                le.flexibleHeight = flexWeight;
                le.flexibleWidth = 1;
                le.layoutPriority = 2; // Override inner LayoutGroup calculations
                return sectionObj;
            }

            var spellInfoSection = MakeSection("spell-info-section", 1.2f, 40);
            var sourceControlsSection = MakeSection("source-controls-section", 0.5f, 30);
            var castersSection = MakeSection("casters-section", 3f, 60);
            var targetsSection = MakeSection("targets-section", 3f, 60);
            _targetsSection = targetsSection.transform;
            var actionBarSection = MakeSection("action-bar-section", 0f, 46);

            var actionVLG = actionBarSection.AddComponent<VerticalLayoutGroup>();
            actionVLG.childForceExpandWidth = true;
            actionVLG.childForceExpandHeight = false;
            actionVLG.childControlHeight = true;
            actionVLG.childControlWidth = true;
            actionVLG.spacing = 4;
            actionVLG.padding = new RectOffset(8, 8, 4, 4);

            currentSpellView = view.widgetCache.Get(spellInfoSection.transform);
            Main.VerboseNotNull(() => currentSpellView);

            currentSpellView.GetComponentInChildren<OwlcatButton>().Interactable = false;
            Main.Verbose("set owlcatbutton to interactable");
            currentSpellView.SetActive(false);
            var currentSpellRect = currentSpellView.transform as RectTransform;
            Main.VerboseNotNull(() => currentSpellRect);
            currentSpellRect.anchorMin = new Vector2(0.25f, 0f);
            currentSpellRect.anchorMax = new Vector2(0.85f, 1f);
            currentSpellRect.offsetMin = Vector2.zero;
            currentSpellRect.offsetMax = Vector2.zero;
            currentSpellView.AddComponent<LayoutElement>().ignoreLayout = true;


            ReactiveProperty<int> SelectedCaster = new ReactiveProperty<int>(-1);

            var actionBarView = UIHelpers.StaticRoot.Find("NestedCanvas1/ActionBarPcView").GetComponent<ActionBarPCView>();
            Main.VerboseNotNull(() => actionBarView);

            var spellPopout = GameObject.Instantiate(actionBarView.m_DragSlot.m_ConvertedView.gameObject, detailsRect);
            spellPopout.DestroyComponents<ActionBarConvertedPCView>();
            spellPopout.DestroyComponents<GridLayoutGroup>();
            spellPopout.Rect().anchoredPosition3D = Vector3.zero;
            spellPopout.Rect().localPosition = Vector3.zero;
            spellPopout.Rect().SetAnchor(0, 0);
            spellPopout.SetActive(true);
            spellPopout.ChildObject("Background").GetComponent<Image>().raycastTarget = true;

            var spellPopGrid = spellPopout.AddComponent<GridLayoutGroup>();
            spellPopGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            spellPopGrid.constraintCount = 1;
            int spellPopWidth = 450;
            if (Language.Locale == Locale.deDE)
                spellPopWidth = 650;
            spellPopGrid.cellSize = new Vector2(spellPopWidth, 40);
            spellPopGrid.padding.left = 25;
            spellPopGrid.padding.top = 12;
            spellPopGrid.padding.bottom = 12;

            GameObject MakeSpellLabel(string text) {
                var labelRoot = GameObject.Instantiate(togglePrefab, spellPopout.transform);
                Main.Verbose($"Label root: {labelRoot == null}");
                labelRoot.DestroyComponents<ToggleWorkaround>();
                labelRoot.DestroyChildren("Background");
                labelRoot.GetComponentInChildren<TextMeshProUGUI>().text = text;
                labelRoot.SetActive(true);

                return labelRoot;
            }

            (ToggleWorkaround toggle, TextMeshProUGUI text) MakeSpellPopoutToggle(string text) {
                var toggleObj = GameObject.Instantiate(togglePrefab, spellPopout.transform);
                toggleObj.SetActive(true);
                toggleObj.Rect().localPosition = Vector3.zero;
                toggleObj.GetComponent<HorizontalLayoutGroup>().childControlWidth = true;
                var label = toggleObj.GetComponentInChildren<TextMeshProUGUI>();
                label.text = text;
                return (toggleObj.GetComponentInChildren<ToggleWorkaround>(), label);
            }

            List<(ToggleWorkaround toggle, TextMeshProUGUI text)> ignoreEffectToggles = new();

            MakeSpellLabel("Ignore effects when checking overwrite:");
            for (int i = 0; i < 8; i++) {
                int index = i;
                var effectToggle = MakeSpellPopoutToggle("BlahblahBuff");
                effectToggle.toggle.gameObject.SetActive(false);
                effectToggle.toggle.onValueChanged.AddListener((ignore) => {
                    try {
                        var buff = view.currentSelectedSpell?.Value;
                        if (buff == null)
                            return;

                        var effect = buff.BuffsApplied.All.Skip(index).First();

                        if (ignore && buff.IgnoreForOverwriteCheck.Add(effect.guid))
                            state.Save();
                        if (!ignore && buff.IgnoreForOverwriteCheck.Remove(effect.guid))
                            state.Save();
                    } catch (Exception e) {
                        Main.Error(e);
                    }
                });
                ignoreEffectToggles.Add(effectToggle);
            }

            var expandSpellPopout = GameObject.Instantiate(expandButtonPrefab, spellInfoSection.transform);
            expandSpellPopout.Rect().pivot = new Vector2(0.5f, 0.5f);
            expandSpellPopout.Rect().SetAnchor(0.92, 0.5);
            expandSpellPopout.AddComponent<LayoutElement>().ignoreLayout = true;
            expandSpellPopout.Rect().anchoredPosition = new Vector2(-20, -10);
            expandSpellPopout.GetComponent<OwlcatButton>().Interactable = true;
            expandSpellPopout.SetActive(true);
            bool isExpanded = false;
            spellPopout.Rect().SetAnchor(0.9, 0.96);
            spellPopout.Rect().anchoredPosition = new Vector2(-20, 0);
            UpdateSpellPopout();

            expandSpellPopout.SetActive(false);

            void UpdateSpellPopout() {
                Main.Safely(() => {
                    expandSpellPopout.ChildRect("Image").DORotate(isExpanded ? Toggles.rotateUp : Toggles.rotateDown, 0.22f).SetUpdate(true);
                    spellPopout.SetActive(isExpanded);
                    if (isExpanded)
                        spellPopout.transform.SetAsLastSibling();
                });
            }

            var expandSpellPopoutButton = expandSpellPopout.GetComponent<OwlcatButton>();
            expandSpellPopoutButton.OnLeftClick.AddListener(() => {
                Main.Safely(() => {
                    isExpanded = !isExpanded;
                    UpdateSpellPopout();
                });
            });

            var casterPopout = GameObject.Instantiate(actionBarView.m_DragSlot.m_ConvertedView.gameObject, content);

            HideCasterPopout = () => {
                view.casterPortraits.ForEach(x => x.SetExpanded(false));
                casterPopout.SetActive(false);
            };
            casterPopout.DestroyComponents<ActionBarConvertedPCView>();
            casterPopout.DestroyComponents<GridLayoutGroup>();
            casterPopout.AddComponent<CanvasGroup>();
            casterPopout.AddComponent<FadeAnimator>();
            casterPopout.Rect().anchoredPosition3D = Vector3.zero;
            casterPopout.Rect().localPosition = Vector3.zero;
            casterPopout.Rect().SetAnchor(0, 0);
            casterPopout.SetActive(false);
            casterPopout.ChildObject("Background").GetComponent<Image>().raycastTarget = true;


            var popGrid = casterPopout.AddComponent<GridLayoutGroup>();
            popGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            popGrid.constraintCount = 1;
            int width = 550;
            if (Language.Locale == Locale.deDE)
                width = 650;
            popGrid.cellSize = new Vector2(width, 40);
            popGrid.padding.left = 25;
            popGrid.padding.top = 12;
            popGrid.padding.bottom = 12;




            GameObject MakeLabel(string text) {
                var labelRoot = GameObject.Instantiate(togglePrefab, casterPopout.transform);
                Main.Verbose($"Label root: {labelRoot == null}");
                labelRoot.DestroyComponents<ToggleWorkaround>();
                labelRoot.DestroyChildren("Background");
                labelRoot.GetComponentInChildren<TextMeshProUGUI>().text = text;
                labelRoot.SetActive(true);

                return labelRoot;
            }

            var hideSpell = MakeToggle(togglePrefab, spellInfoSection.transform, 0.03f, 0.5f, "hideability".i8(), "hide-spell");
            hideSpell.AddComponent<LayoutElement>().ignoreLayout = true;
            hideSpell.transform.SetSiblingIndex(0);
            hideSpell.SetActive(false);
            hideSpell.Rect().pivot = new Vector2(0, 0.5f);
            var hideSpellToggle = hideSpell.GetComponentInChildren<ToggleWorkaround>();

            // Add/Remove row — vertical stack on left side of targets section
            var addRemoveRow = new GameObject("add-remove-row", typeof(RectTransform));
            addRemoveRow.GetComponent<RectTransform>().SetParent(targetsSection.transform, false);
            var addRemoveVLG = addRemoveRow.AddComponent<VerticalLayoutGroup>();
            addRemoveVLG.childForceExpandWidth = true;
            addRemoveVLG.childForceExpandHeight = false;
            addRemoveVLG.childControlWidth = true;
            addRemoveVLG.childControlHeight = true;
            addRemoveVLG.spacing = 4;
            addRemoveVLG.padding = new RectOffset(2, 2, 4, 4);
            addRemoveVLG.childAlignment = TextAnchor.MiddleCenter;
            var addRemoveRect = addRemoveRow.GetComponent<RectTransform>();
            addRemoveRect.anchorMin = new Vector2(0, 0);
            addRemoveRect.anchorMax = new Vector2(0.25f, 1);
            addRemoveRect.offsetMin = new Vector2(4, 4);
            addRemoveRect.offsetMax = new Vector2(-2, -4);

            view.addToAll = GameObject.Instantiate(buttonPrefab, addRemoveRow.transform);
            view.addToAll.GetComponentInChildren<TextMeshProUGUI>().text = "add-all".i8();
            var addRect = view.addToAll.Rect();
            addRect.anchorMin = Vector2.zero;
            addRect.anchorMax = Vector2.one;
            addRect.pivot = new Vector2(0.5f, 0.5f);
            addRect.offsetMin = Vector2.zero;
            addRect.offsetMax = Vector2.zero;

            view.removeFromAll = GameObject.Instantiate(buttonPrefab, addRemoveRow.transform);
            view.removeFromAll.GetComponentInChildren<TextMeshProUGUI>().text = "remove-all".i8();
            var remRect = view.removeFromAll.Rect();
            remRect.anchorMin = Vector2.zero;
            remRect.anchorMax = Vector2.one;
            remRect.pivot = new Vector2(0.5f, 0.5f);
            remRect.offsetMin = Vector2.zero;
            remRect.offsetMax = Vector2.zero;

            view.addToAll.GetComponentInChildren<OwlcatButton>().OnLeftClick.AddListener(() => {
                var buff = view.Selected;
                if (buff == null) return;

                for (int i = 0; i < Bubble.Group.Count && i < view.targets.Length; i++) {
                    if (view.targets[i].Button.Interactable && !buff.UnitWants(Bubble.Group[i])) {
                        buff.SetUnitWants(Bubble.Group[i], true);
                    }
                }
                state.Recalculate(true);

            });
            view.removeFromAll.GetComponentInChildren<OwlcatButton>().OnLeftClick.AddListener(() => {
                var buff = view.Selected;
                if (buff == null) return;

                for (int i = 0; i < Bubble.Group.Count && i < view.targets.Length; i++) {
                    if (buff.UnitWants(Bubble.Group[i])) {
                        buff.SetUnitWants(Bubble.Group[i], false);
                    }
                }
                state.Recalculate(true);
            });





            var capLabel = MakeLabel("  " + "limitcasts".i8());

            var (blacklistToggle, _) = MakePopoutToggle("bancasts".i8());
            var (powerfulChangeToggle, powerfulChangeLabel) = MakePopoutToggle("use.powerfulchange".i8());
            var (shareTransmutationToggle, shareTransmutationLabel) = MakePopoutToggle("use.sharetransmutation".i8());
            var (reservoirCLBuffToggle, reservoirCLBuffLabel) = MakePopoutToggle("use.reservoirclbuff".i8());
            var (azataZippyMagicToggle, azataZippyMagicLabel) = MakePopoutToggle("use.azatazippymagic".i8());
            var defaultLabelColor = shareTransmutationLabel.color;

            (ToggleWorkaround, TextMeshProUGUI) MakePopoutToggle(string text) {
                var toggleObj = GameObject.Instantiate(togglePrefab, casterPopout.transform);
                toggleObj.SetActive(true);
                toggleObj.Rect().localPosition = Vector3.zero;
                toggleObj.GetComponent<HorizontalLayoutGroup>().childControlWidth = true;
                var label = toggleObj.GetComponentInChildren<TextMeshProUGUI>();
                label.text = text;
                return (toggleObj.GetComponentInChildren<ToggleWorkaround>(), label);

            }
            MakeLabel("warn.arcanepool".i8());

            float capChangeScale = 0.7f;
            var decreaseCustomCap = GameObject.Instantiate(expandButtonPrefab, capLabel.transform);
            decreaseCustomCap.Rect().localScale = new Vector3(capChangeScale, capChangeScale, capChangeScale);
            decreaseCustomCap.Rect().pivot = new Vector2(.5f, .5f);
            decreaseCustomCap.Rect().SetRotate2D(90);
            decreaseCustomCap.Rect().anchoredPosition = Vector2.zero;
            decreaseCustomCap.SetActive(true);
            var decreaseCustomCapButton = decreaseCustomCap.GetComponent<OwlcatButton>();

            var capValueLabel = GameObject.Instantiate(togglePrefab.GetComponentInChildren<TextMeshProUGUI>().gameObject, capLabel.transform);
            var capValueText = capValueLabel.GetComponent<TextMeshProUGUI>();
            capValueText.text = "nolimit".i8();
            //capValueLabel.AddComponent<LayoutElement>().preferredWidth = 80;

            var increaseCustomCap = GameObject.Instantiate(expandButtonPrefab, capLabel.transform);
            increaseCustomCap.Rect().pivot = new Vector2(.5f, .5f);
            increaseCustomCap.Rect().localScale = new Vector3(capChangeScale, capChangeScale, capChangeScale);
            increaseCustomCap.Rect().SetRotate2D(-90);
            increaseCustomCap.Rect().anchoredPosition = Vector2.zero;
            increaseCustomCap.SetActive(true);
            var increaseCustomCapButton = increaseCustomCap.GetComponent<OwlcatButton>();


            void AdjustCap(int delta) {
                var buff = view.currentSelectedSpell.Value;
                if (buff == null) return;
                if (SelectedCaster.Value < 0) return;

                buff.AdjustCap(SelectedCaster.Value, delta);
                state.Recalculate(true);
            }

            decreaseCustomCapButton.OnLeftClick.AddListener(() => {
                AdjustCap(-1);
            });
            increaseCustomCapButton.OnLeftClick.AddListener(() => {
                AdjustCap(1);
            });

            decreaseCustomCapButton.Interactable = false;
            increaseCustomCapButton.Interactable = false;

            view.casterPortraits = new Portrait[totalCasters];

            shareTransmutationToggle.onValueChanged.AddListener(allow => {
                if (SelectedCaster.Value >= 0 && view.Get(out var buff)) {
                    buff.CasterQueue[SelectedCaster.Value].ShareTransmutation = allow;
                    state.Recalculate(true);
                }
            });
            powerfulChangeToggle.onValueChanged.AddListener(allow => {
                if (SelectedCaster.Value >= 0 && view.Get(out var buff)) {
                    buff.CasterQueue[SelectedCaster.Value].PowerfulChange = allow;
                    state.Recalculate(true);
                }
            });
            reservoirCLBuffToggle.onValueChanged.AddListener(allow => {
                if (SelectedCaster.Value >= 0 && view.Get(out var buff)) {
                    buff.CasterQueue[SelectedCaster.Value].ReservoirCLBuff = allow;
                    state.Recalculate(true);
                }
            });
            azataZippyMagicToggle.onValueChanged.AddListener(allow => {
                if (SelectedCaster.Value >= 0 && view.Get(out var buff)) {
                    buff.CasterQueue[SelectedCaster.Value].AzataZippyMagic = allow;
                    state.Recalculate(true);
                }
            });

            blacklistToggle.onValueChanged.AddListener((blacklisted) => {
                Main.Log($"blacklisting, buff={view.currentSelectedSpell != null}, caster={SelectedCaster.Value}");
                var buff = view.currentSelectedSpell?.Value;
                if (buff == null)
                    return;
                if (SelectedCaster.Value < 0)
                    return;

                var caster = buff.CasterQueue[SelectedCaster.value];

                Main.Log("caster banned => " + blacklisted);

                if (blacklisted != caster.Banned) {
                    caster.Banned = blacklisted;
                    state.Recalculate(true);
                }
            });

            // Source controls — anchor-based split: priority left, toggles right
            var sourceControlObj = sourceControlsSection;
            sourceControlObj.SetActive(false); // hidden until buff selected

            // Left side — priority + extend rod toggle (left 55%)
            var (prioSideObj, prioSideRect) = UIHelpers.Create("prio-side", sourceControlObj.transform);
            prioSideRect.anchorMin = new Vector2(0, 0);
            prioSideRect.anchorMax = new Vector2(0.55f, 1);
            prioSideRect.offsetMin = new Vector2(20, 2);
            prioSideRect.offsetMax = new Vector2(0, -2);
            var prioVLG = prioSideObj.AddComponent<VerticalLayoutGroup>();
            prioVLG.childForceExpandHeight = false;
            prioVLG.childForceExpandWidth = true;
            prioVLG.childControlHeight = true;
            prioVLG.childControlWidth = true;
            prioVLG.spacing = 4;

            // Right side — toggles (right 45%)
            var (toggleSideObj, toggleSideRect) = UIHelpers.Create("toggle-side", sourceControlObj.transform);
            toggleSideRect.anchorMin = new Vector2(0.55f, 0);
            toggleSideRect.anchorMax = new Vector2(1, 1);
            toggleSideRect.offsetMin = new Vector2(0, 2);
            toggleSideRect.offsetMax = new Vector2(-4, -2);
            var toggleVLG = toggleSideObj.AddComponent<VerticalLayoutGroup>();
            toggleVLG.childForceExpandHeight = false;
            toggleVLG.childForceExpandWidth = true;
            toggleVLG.childControlHeight = true;
            toggleVLG.childControlWidth = true;
            toggleVLG.spacing = 2;
            toggleVLG.childAlignment = TextAnchor.UpperRight;

            float srcToggleScale = 0.7f;
            GameObject MakeSourceToggle(string label) {
                var toggleObj = GameObject.Instantiate(togglePrefab, toggleSideObj.transform);
                toggleObj.SetActive(true);
                toggleObj.transform.localScale = new Vector3(srcToggleScale, srcToggleScale, srcToggleScale);
                toggleObj.GetComponentInChildren<TextMeshProUGUI>().text = label;
                return toggleObj;
            }

            var useSpellsObj = MakeSourceToggle("use.spells".i8());
            var useScrollsObj = MakeSourceToggle("use.scrolls".i8());
            var usePotionsObj = MakeSourceToggle("use.potions".i8());
            var useEquipmentObj = MakeSourceToggle("use.equipment".i8());

            var useSpellsToggle = useSpellsObj.GetComponentInChildren<ToggleWorkaround>();
            var useScrollsToggle = useScrollsObj.GetComponentInChildren<ToggleWorkaround>();
            var usePotionsToggle = usePotionsObj.GetComponentInChildren<ToggleWorkaround>();
            var useEquipmentToggle = useEquipmentObj.GetComponentInChildren<ToggleWorkaround>();

            useSpellsToggle.onValueChanged.AddListener(val => {
                var b = view.Selected;
                if (b != null) { b.UseSpells = val; if (b.SavedState != null) b.SavedState.UseSpells = val; state.Save(); }
            });
            useScrollsToggle.onValueChanged.AddListener(val => {
                var b = view.Selected;
                if (b != null) { b.UseScrolls = val; if (b.SavedState != null) b.SavedState.UseScrolls = val; state.Save(); }
            });
            usePotionsToggle.onValueChanged.AddListener(val => {
                var b = view.Selected;
                if (b != null) { b.UsePotions = val; if (b.SavedState != null) b.SavedState.UsePotions = val; state.Save(); }
            });
            useEquipmentToggle.onValueChanged.AddListener(val => {
                var b = view.Selected;
                if (b != null) { b.UseEquipment = val; if (b.SavedState != null) b.SavedState.UseEquipment = val; state.Save(); }
            });

            // Per-buff source priority override
            string GetPriorityText(int overrideVal) {
                if (overrideVal < 0) return "priority.useglobal".i8();
                return SourcePriorityKeys[overrideVal].i8();
            }

            // Priority row — clickable text that cycles through priority options
            var prioLabelObj = new GameObject("prio-label", typeof(RectTransform));
            var prioLabelRect = prioLabelObj.GetComponent<RectTransform>();
            prioLabelRect.SetParent(prioSideObj.transform, false);
            var prioLE = prioLabelObj.AddComponent<LayoutElement>();
            prioLE.preferredHeight = 24;
            prioLE.flexibleWidth = 1;
            var prioOverrideText = prioLabelObj.AddComponent<TextMeshProUGUI>();
            prioOverrideText.text = $"{"setting-source-priority".i8()}: {"priority.useglobal".i8()}";
            prioOverrideText.fontSize = 16;
            prioOverrideText.color = new Color(0.2f, 0.15f, 0.1f, 1f);
            prioOverrideText.alignment = TextAlignmentOptions.Left;
            prioOverrideText.fontStyle = FontStyles.Underline;
            prioOverrideText.enableWordWrapping = true;
            prioOverrideText.overflowMode = TextOverflowModes.Truncate;
            prioOverrideText.raycastTarget = true;
            var prioButton = prioLabelObj.AddComponent<Button>();
            prioButton.transition = Selectable.Transition.None;
            prioButton.onClick.AddListener(() => {
                var b = view.Selected;
                if (b == null) return;
                b.SourcePriorityOverride = b.SourcePriorityOverride >= 5 ? -1 : b.SourcePriorityOverride + 1;
                if (b.SavedState != null) b.SavedState.SourcePriorityOverride = b.SourcePriorityOverride;
                prioOverrideText.text = $"{"setting-source-priority".i8()}: {GetPriorityText(b.SourcePriorityOverride)}";
                b.SortProviders();
                state.Save();
            });

            // Extend Rod toggle — on left side, below priority
            var useExtendRodObj = MakeSourceToggle("use.extendrod".i8());
            useExtendRodObj.transform.SetParent(prioSideObj.transform, false);
            var useExtendRodToggle = useExtendRodObj.GetComponentInChildren<ToggleWorkaround>();

            useExtendRodToggle.onValueChanged.AddListener(val => {
                var b = view.Selected;
                if (b != null) { b.UseExtendRod = val; if (b.SavedState != null) b.SavedState.UseExtendRod = val; state.Save(); }
            });

            const float groupHeight = 90f;
            var (groupHolder, castersRect) = UIHelpers.Create("CastersHolder", castersSection.transform);
            view.castersHolder = groupHolder;
            castersRect.SetParent(castersSection.transform, false);
            groupHolder.MakeComponent<ContentSizeFitter>(f => {
                f.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            });
            castersRect.anchorMin = new Vector2(0.5f, 0f);
            castersRect.anchorMax = new Vector2(0.5f, 1f);
            castersRect.pivot = new Vector2(0.5f, 0.5f);
            castersRect.offsetMin = new Vector2(0, 4);
            castersRect.offsetMax = new Vector2(0, -4);

            var horizontalGroup = groupHolder.AddComponent<HorizontalLayoutGroup>();
            horizontalGroup.spacing = 6;
            horizontalGroup.childControlHeight = true;
            horizontalGroup.childForceExpandHeight = false;
            horizontalGroup.childAlignment = TextAnchor.MiddleCenter;

            var (selfCastInfoObj, selfCastInfoRect) = UIHelpers.Create("SelfCastInfo", castersSection.transform);
            selfCastInfoRect.FillParent();
            view.selfCastInfoLabel = selfCastInfoObj.AddComponent<TextMeshProUGUI>();
            view.selfCastInfoLabel.text = "tooltip.source.selfonly".i8();
            view.selfCastInfoLabel.fontSize = 36;
            view.selfCastInfoLabel.alignment = TextAlignmentOptions.Center;
            view.selfCastInfoLabel.color = Color.black;
            view.selfCastInfoLabel.fontStyle = FontStyles.Italic;
            selfCastInfoObj.SetActive(false);
            selfCastInfoObj.transform.SetAsLastSibling();

            for (int i = 0; i < totalCasters; i++) {
                var portrait = CreatePortrait(groupHeight, castersRect, true, true, view.casterPortraits, casterPopout);
                view.casterPortraits[i] = portrait;
                portrait.Image.color = Color.yellow;

                portrait.Text.fontSizeMax = 18;
                portrait.Text.alignment = TextAlignmentOptions.Center;
                portrait.Text.fontSize = 18;
                portrait.Text.color = Color.black;
                portrait.Text.gameObject.transform.parent.gameObject.SetActive(true);
                portrait.Text.text = "12/12";
                int casterIndex = i;

                portrait.Expand?.OnLeftClick.AddListener(() => {
                    if (portrait.State) {
                        SelectedCaster.Value = (view.casterPortraitMap != null && casterIndex < view.casterPortraitMap.Length)
                            ? view.casterPortraitMap[casterIndex]
                            : casterIndex;
                        UpdateDetailsView();
                    } else {
                        SelectedCaster.Value = -1;
                    }
                });
            }

            // Position on the window root (content.parent) so checkboxes align
            // with the gear/settings button which is also on the window root.
            var groupObj = new GameObject("buff-group", typeof(RectTransform));
            var groupRect = groupObj.GetComponent<RectTransform>();
            groupRect.SetParent(content.parent, false);
            groupRect.anchorMin = new Vector2(0.40f, 0.10f);
            groupRect.anchorMax = new Vector2(0.80f, 0.15f);
            groupRect.offsetMin = Vector2.zero;
            groupRect.offsetMax = Vector2.zero;
            var buffGroupHLG = groupObj.AddComponent<HorizontalLayoutGroup>();
            buffGroupHLG.childForceExpandWidth = true;
            buffGroupHLG.childForceExpandHeight = true;
            buffGroupHLG.childControlWidth = true;
            buffGroupHLG.childControlHeight = true;
            buffGroupHLG.spacing = 8;
            buffGroupHLG.padding = new RectOffset(8, 8, 0, 0);
            groupObj.SetActive(false);

            float groupToggleScale = 0.7f;
            GameObject MakeGroupToggle(string label) {
                var toggleObj = GameObject.Instantiate(togglePrefab, groupRect);
                toggleObj.SetActive(true);
                toggleObj.transform.localScale = new Vector3(groupToggleScale, groupToggleScale, groupToggleScale);
                toggleObj.GetComponentInChildren<TextMeshProUGUI>().text = label;
                return toggleObj;
            }

            var groupNormalObj = MakeGroupToggle("group.normal.btn".i8());
            var groupImportantObj = MakeGroupToggle("group.important.btn".i8());
            var groupQuickObj = MakeGroupToggle("group.short.btn".i8());

            var groupNormalToggle = groupNormalObj.GetComponentInChildren<ToggleWorkaround>();
            var groupImportantToggle = groupImportantObj.GetComponentInChildren<ToggleWorkaround>();
            var groupQuickToggle = groupQuickObj.GetComponentInChildren<ToggleWorkaround>();

            groupNormalToggle.onValueChanged.AddListener(val => {
                if (view.Get(out var buff)) {
                    if (val) buff.InGroups.Add(BuffGroup.Long);
                    else buff.InGroups.Remove(BuffGroup.Long);
                    state.Save();
                }
            });

            groupImportantToggle.onValueChanged.AddListener(val => {
                if (view.Get(out var buff)) {
                    if (val) buff.InGroups.Add(BuffGroup.Important);
                    else buff.InGroups.Remove(BuffGroup.Important);
                    state.Save();
                }
            });

            groupQuickToggle.onValueChanged.AddListener(val => {
                if (view.Get(out var buff)) {
                    if (val) buff.InGroups.Add(BuffGroup.Quick);
                    else buff.InGroups.Remove(BuffGroup.Quick);
                    state.Save();
                }
            });

            hideSpellToggle.onValueChanged.AddListener(shouldHide => {
                if (view.Get(out var buff)) {
                    buff.SetHidden(HideReason.Blacklisted, shouldHide);
                    state.Save();
                    RefreshFiltering();
                }
            });

            UpdateDetailsView = () => {
                bool hasBuff = view.Get(out var buff);

                groupRect.gameObject.SetActive(hasBuff);
                hideSpell.SetActive(hasBuff);
                expandSpellPopout.SetActive(hasBuff);
                if (!hasBuff) {
                    isExpanded = false;
                    UpdateSpellPopout();
                }

                if (!hasBuff) {
                    sourceControlObj.SetActive(false);
                    return;
                }

                groupNormalToggle.isOn = buff.InGroups.Contains(BuffGroup.Long);
                groupImportantToggle.isOn = buff.InGroups.Contains(BuffGroup.Important);
                groupQuickToggle.isOn = buff.InGroups.Contains(BuffGroup.Quick);
                hideSpellToggle.isOn = buff.HideBecause(HideReason.Blacklisted);

                var effects = buff.BuffsApplied.All.ToArray();

                for (int i = 0; i < ignoreEffectToggles.Count; i++) {
                    if (i < effects.Length) {
                        ignoreEffectToggles[i].toggle.gameObject.SetActive(true);
                        ignoreEffectToggles[i].text.text = effects[i].name;
                        ignoreEffectToggles[i].toggle.isOn = buff.IgnoreForOverwriteCheck.Contains(effects[i].guid);
                    } else {
                        ignoreEffectToggles[i].toggle.gameObject.SetActive(false);
                    }
                }

                bool hasSpellProviders = buff.CasterQueue.Any(c => c.SourceType == BuffSourceType.Spell);
                bool hasScrollProviders = buff.CasterQueue.Any(c => c.SourceType == BuffSourceType.Scroll);
                bool hasPotionProviders = buff.CasterQueue.Any(c => c.SourceType == BuffSourceType.Potion);
                bool hasEquipmentProviders = buff.CasterQueue.Any(c => c.SourceType == BuffSourceType.Equipment);

                useSpellsObj.SetActive(hasSpellProviders);
                if (hasSpellProviders)
                    useSpellsToggle.isOn = buff.UseSpells;

                useScrollsObj.SetActive(hasScrollProviders);
                if (hasScrollProviders)
                    useScrollsToggle.isOn = buff.UseScrolls;

                usePotionsObj.SetActive(hasPotionProviders);
                if (hasPotionProviders)
                    usePotionsToggle.isOn = buff.UsePotions;

                useEquipmentObj.SetActive(hasEquipmentProviders);
                if (hasEquipmentProviders)
                    useEquipmentToggle.isOn = buff.UseEquipment;

                // Extend Rod toggle — always visible when source controls are shown
                useExtendRodToggle.isOn = buff.UseExtendRod;

                bool isEquipmentCategory = CurrentCategory.Value == Category.Equipment;
                int sourceCount = (hasSpellProviders ? 1 : 0) + (hasScrollProviders ? 1 : 0) + (hasPotionProviders ? 1 : 0) + (hasEquipmentProviders ? 1 : 0);
                sourceControlObj.SetActive(!isEquipmentCategory && (sourceCount > 1 || hasSpellProviders));

                prioOverrideText.text = $"{"setting-source-priority".i8()}: {GetPriorityText(buff.SourcePriorityOverride)}";

                if (SelectedCaster.Value >= 0 && casterPopout.activeSelf) {
                    var who = buff.CasterQueue[SelectedCaster.value];
                    int actualCap = who.CustomCap < 0 ? who.MaxCap : who.CustomCap;
                    if (who.MaxCap < 100)
                        capValueText.text = $"{actualCap}/{who.MaxCap}";
                    else
                        capValueText.text = $"available.atwill".i8();

                    blacklistToggle.isOn = who.Banned;
                    shareTransmutationToggle.isOn = who.ShareTransmutation;
                    powerfulChangeToggle.isOn = who.PowerfulChange;
                    reservoirCLBuffToggle.isOn = who.ReservoirCLBuff;

                    var skidmarkable = who.spell.IsArcanistSpell && who.spell.Blueprint.School == Kingmaker.Blueprints.Classes.Spells.SpellSchool.Transmutation;
                    shareTransmutationToggle.interactable = skidmarkable && who.who.HasFact(ShareTransmutationFeature);
                    powerfulChangeToggle.interactable = skidmarkable && who.who.HasFact(PowerfulChangeFeature);
                    reservoirCLBuffToggle.interactable = (who.spell.IsArcanistSpell || (who.spell.Spellbook.Blueprint == SpellTools.Spellbook.ExploiterWizardSpellbook))
                        && who.who.HasFact(BubbleBlueprints.ReservoirBaseAbility) && !who.who.Progression.IsArchetype(BubbleBlueprints.PhantasmalMageArchetype);

                    shareTransmutationLabel.color = shareTransmutationToggle.interactable ? defaultLabelColor : Color.gray;
                    powerfulChangeLabel.color = powerfulChangeToggle.interactable ? defaultLabelColor : Color.gray;
                    reservoirCLBuffLabel.color = reservoirCLBuffToggle.interactable ? defaultLabelColor : Color.gray;

                    increaseCustomCapButton.Interactable = who.AvailableCredits < 100 && who.CustomCap != -1;
                    decreaseCustomCapButton.Interactable = who.AvailableCredits < 100 && who.CustomCap != 0;

                    // Azata Zippy Magic
                    azataZippyMagicToggle.isOn = who.AzataZippyMagic;

                    var hasAzataZippyMagicFact = who.who.HasFact(AzataZippyMagicFeature);
                    var isSpellMass = buff.IsMass;
                    var canCastOnOthers = shareTransmutationToggle.isOn || !who.SelfCastOnly;

                    azataZippyMagicToggle.interactable = hasAzataZippyMagicFact && !isSpellMass && canCastOnOthers;
                    azataZippyMagicLabel.color = azataZippyMagicToggle.interactable ? defaultLabelColor : Color.gray;

                }

            };
        }


        private int totalCasters = 0;
        private GameObject currentSpellView;
        private SearchBar search;

        private void MakeGroupHolder(GameObject portraitPrefab, GameObject expandButtonPrefab, GameObject buttonPrefab, Transform content) {
            var groupHolder = new GameObject("GroupHolder", typeof(RectTransform));
            var groupRect = groupHolder.GetComponent<RectTransform>();
            groupRect.AddTo(content);

            const float groupHeight = 100f;

            groupRect.anchorMin = new Vector2(0.25f, 0f);
            groupRect.anchorMax = new Vector2(1f, 1f);
            groupRect.pivot = new Vector2(0.5f, 0.5f);
            groupRect.offsetMin = new Vector2(2, 4);
            groupRect.offsetMax = new Vector2(-4, -4);

            var horizontalGroup = groupHolder.AddComponent<HorizontalLayoutGroup>();
            horizontalGroup.spacing = 6;
            horizontalGroup.childControlHeight = true;
            horizontalGroup.childForceExpandHeight = false;
            horizontalGroup.childControlWidth = true;
            horizontalGroup.childForceExpandWidth = true;
            horizontalGroup.childAlignment = TextAnchor.MiddleCenter;

            view.targets = new Portrait[Bubble.Group.Count];

            for (int i = 0; i < Bubble.Group.Count; i++) {
                Portrait portrait = CreatePortrait(groupHeight, groupRect, false, false);

                portrait.GameObject.SetActive(true);
                var aspect = portrait.GameObject.AddComponent<AspectRatioFitter>();
                aspect.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
                aspect.aspectRatio = 0.75f;

                portrait.Image.sprite = Bubble.Group[i].Portrait.SmallPortrait;

                int personIndex = i;

                portrait.Button.OnLeftClick.AddListener(() => {
                    UnitEntityData me = Bubble.Group[personIndex];
                    var buff = view.Selected;
                    if (buff == null)
                        return;

                    if (!buff.CanTarget(me))
                        return;

                    if (buff.UnitWants(me)) {
                        buff.SetUnitWants(me, false);
                    } else {
                        buff.SetUnitWants(me, true);
                    }

                    try {
                        state.Recalculate(true);
                    } catch (Exception ex) {
                        Main.Error(ex, "Recalculating spell list?");
                    }

                });
                view.targets[i] = portrait;
            }
        }

        private void ShowBuffWindow() {
            Bubble.RefreshGroup();

            if (WindowCreated && view.targets.Length != Bubble.Group.Count) {
                Main.Verbose("Group size changed, rebuilding window");
                foreach (Transform child in Root.transform) {
                    GameObject.Destroy(child.gameObject);
                }
                WindowCreated = false;
            }

            if (!WindowCreated) {
                try {
                    CreateWindow();
                } catch (Exception ex) {
                    Main.Error(ex, "Creating window?");
                }
            }
            state.Recalculate(true);
            RefreshFiltering();
            Root.SetActive(true);
            FadeIn(Root);
        }

        public void Destroy() {
            GameObject.Destroy(Root);
            GameObject.Destroy(ToggleButton);
        }

        private void OnDestroy() {
        }





        internal void Execute(BuffGroup group) {
            UnitBuffPartView.StartSuppression();
            Executor.Execute(group);
            BubbleBuffGlobalController.Instance.Invoke(nameof(BubbleBuffGlobalController.EndSuppression), 1.0f);
        }



        internal void RevalidateSpells() {
            if (state.GroupIsDirty(Bubble.ConfigGroup)) {
                AbilityCache.Revalidate();
            }

            state.InputDirty = true;
        }


    }

    public class CasterCacheEntry {
        public Ability PowerfulChange;
        public Ability ShareTransmutation;
        public Ability ReservoirCLBuff;
    }

    public class AbilityCache {

        public static Dictionary<string, CasterCacheEntry> CasterCache = new();

        public static void Revalidate() {
            Main.Verbose("Revalidating Caster Cache");
            CasterCache.Clear();
            foreach (var u in Bubble.ConfigGroup) {
                var entry = new CasterCacheEntry {
                    PowerfulChange = u.Abilities.GetAbility(BubbleBlueprints.PowerfulChange),
                    ShareTransmutation = u.Abilities.GetAbility(BubbleBlueprints.ShareTransmutation),
                    ReservoirCLBuff = u.Abilities.GetAbility(BubbleBlueprints.ReservoirBaseAbility)
                };
                CasterCache[u.UniqueId] = entry;
            }
        }
    }

    [HarmonyPatch(typeof(ModificatorsBaseView), nameof(ModificatorsBaseView.Initialize))]
    static class NudgeModificators {
        [HarmonyPostfix]
        public static void Initialize(ModificatorsBaseView __instance) {
            if (__instance.gameObject.transform is not RectTransform rect) return;

            rect.localPosition += new Vector3(0, 70, 0);
        }
    }


    [HarmonyPatch(typeof(Game), nameof(Game.ResetUI))]
    static class ReinstallUIOnControllerModeChange {
        [HarmonyPostfix]
        public static void ResetUI(bool isGameInputChange) {
            if (!isGameInputChange) return;
            Main.Log("[ResetUI] Input mode changed — reinstalling BubbleBuffs");
            GlobalBubbleBuffer.Instance?.TryInstallUI();
        }
    }

    //[HarmonyPatch(typeof(UnitBuffPartPCView), "DrawBuffs")]
    static class UnitBuffPartView {

        private static bool suppress;

        public static void StartSuppression() {
            suppress = true;
        }

        public static void EndSuppresion() {
            suppress = false;
            int count = toUpdate.Count;
            foreach (var view in toUpdate)
                view.DrawBuffs();
            toUpdate.Clear();
            Main.Verbose($"Suppressed {suppressed} draws across {count} views");
            suppressed = 0;
        }

        private static int suppressed = 0;

        private static HashSet<UnitBuffPartPCView> toUpdate = new();

        static bool Prefix(UnitBuffPartPCView __instance) {
            if (suppress) {
                suppressed++;
                toUpdate.Add(__instance);
                return false;
            }

            __instance.Clear();
            bool flag = __instance.ViewModel.Buffs.Count > 6;
            __instance.m_AdditionalTrigger.gameObject.SetActive(flag);
            int num = 0;
            foreach (BuffVM viewModel in __instance.ViewModel.Buffs) {
                BuffPCView widget = WidgetFactory.GetWidget<BuffPCView>(__instance.m_BuffView, true);
                widget.Bind(viewModel);
                if (flag && num >= 5) {
                    widget.transform.SetParent(__instance.m_AdditionalContainer, false);
                } else {
                    widget.transform.SetParent(__instance.m_MainContainer, false);
                }
                num++;
                __instance.m_BuffList.Add(widget);
            }

            return false;
        }

    }

    class TooltipTemplateBuffer : TooltipBaseTemplate {
        public class BuffResult {
            public BubbleBuff buff;
            public List<string> messages;
            public int count;
            public Dictionary<BuffSourceType, int> sourceCounts = new();
            public bool ExtendRodUsed;
            public BuffResult(BubbleBuff buff) {
                this.buff = buff;
            }
        };
        private List<BuffResult> good = new();
        private List<BuffResult> bad = new();
        private List<BuffResult> skipped = new();

        public BuffResult AddBad(BubbleBuff buff) {
            BuffResult result = new(buff);
            result.messages = new();
            bad.Add(result);
            return result;
        }
        public BuffResult AddSkip(BubbleBuff buff) {
            BuffResult result = new(buff);
            skipped.Add(result);
            return result;
        }
        public BuffResult AddGood(BubbleBuff buff) {
            BuffResult result = new(buff);
            good.Add(result);
            return result;
        }

        public override IEnumerable<ITooltipBrick> GetHeader(TooltipTemplateType type) {
            yield return new TooltipBrickEntityHeader($"BubbleBuff {"tooltip.results".i8()}", null);
            yield break;
        }
        public override IEnumerable<ITooltipBrick> GetBody(TooltipTemplateType type) {
            List<ITooltipBrick> elements = new();
            AddResultsNoMessages("tooltip.applied".i8(), elements, good);
            AddResultsNoMessages("tooltip.skipped".i8(), elements, skipped);

            if (!bad.Empty()) {
                elements.Add(new TooltipBrickTitle("tooltip.failed".i8()));
                elements.Add(new TooltipBrickSeparator());

                foreach (var r in bad) {
                    elements.Add(new TooltipBrickIconAndName(r.buff.Spell.Icon, $"<b>{r.buff.Name}</b>", TooltipBrickElementType.Small));
                    foreach (var msg in r.messages)
                        elements.Add(new TooltipBrickText("   " + msg));

                }
            }

            return elements;
        }

        private void AddResultsNoMessages(string title, List<ITooltipBrick> elements, List<BuffResult> result) {
            if (!result.Empty()) {
                elements.Add(new TooltipBrickTitle(title));
                elements.Add(new TooltipBrickSeparator());
                foreach (var r in result) {
                    string label = $"<b>{r.buff.NameMeta}</b> x{r.count}";
                    if (r.sourceCounts.Count >= 1) {
                        var parts = new List<string>();
                        foreach (var kv in r.sourceCounts) {
                            string sourceLabel = kv.Key switch {
                                BuffSourceType.Spell => "source.spell".i8(),
                                BuffSourceType.Scroll => "source.scroll".i8(),
                                BuffSourceType.Potion => "source.potion".i8(),
                                BuffSourceType.Equipment => "source.equipment".i8(),
                                _ => kv.Key.ToString()
                            };
                            parts.Add($"{sourceLabel}: {kv.Value}");
                        }
                        label += $" ({string.Join(", ", parts)})";
                    }
                    if (r.ExtendRodUsed)
                        label += $" [{"log.extend-rod-applied".i8()}]";
                    elements.Add(new TooltipBrickIconAndName(r.buff.Spell.Icon, label, TooltipBrickElementType.Small));
                }
            }
        }
    }

    class ServiceWindowWatcher : IUIEventHandler {
        public void HandleUIEvent(UIEventType type) {
            GlobalBubbleBuffer.Instance.SpellbookController?.Hide();
        }
    }

    class SyncBubbleHud : MonoBehaviour {
        private GameObject bubbleHud => GlobalBubbleBuffer.Instance.bubbleHud;
        private CanvasGroup src;
        // When re-enabled after controller mode, alpha may still be 0 from a prior fade-out.
        // Suppress alpha-based hiding until alpha has recovered to > 0.9 at least once.
        private bool waitingForAlpha = false;
        private float lastAlphaLogged = -1f;

        private void Awake() {
            src = GetComponent<CanvasGroup>();
            Main.Verbose($"[SyncBubbleHud] Awake on '{gameObject.name}', src={(src == null ? "NULL" : "ok")}");
        }

        private void Update() {
            if (bubbleHud == null) return;

            float alpha = src != null ? src.alpha : -1f;
            if (Mathf.Abs(alpha - lastAlphaLogged) > 0.05f) {
                Main.Verbose($"[SyncBubbleHud] alpha={alpha:F2}, bubbleHud.activeSelf={bubbleHud.activeSelf}, waitingForAlpha={waitingForAlpha}");
                lastAlphaLogged = alpha;
            }

            if (src.alpha < 0.1) {
                if (!waitingForAlpha)
                    bubbleHud.SetActive(false);
            } else if (src.alpha > 0.9) {
                waitingForAlpha = false;
                bubbleHud.SetActive(true);
            }
        }

        private void OnEnable() {
            Main.Verbose($"[SyncBubbleHud] OnEnable, bubbleHud={(bubbleHud == null ? "NULL" : $"activeSelf={bubbleHud.activeSelf}")}, src.alpha={src?.alpha:F2}");
            waitingForAlpha = true;
            if (bubbleHud != null && !bubbleHud.activeSelf)
                bubbleHud.SetActive(true);
        }

        private void OnDisable() {
            Main.Verbose($"[SyncBubbleHud] OnDisable, bubbleHud={(bubbleHud == null ? "NULL" : $"activeSelf={bubbleHud.activeSelf}")}");
            waitingForAlpha = false;
            if (bubbleHud != null && bubbleHud.activeSelf)
                bubbleHud?.SetActive(false);
        }

        private void OnDestroy() {
            Main.Verbose($"[SyncBubbleHud] OnDestroy — component was destroyed on '{gameObject.name}'");
        }

        public void Destroy() { }

    }

    class GlobalBubbleBuffer {
        public BubbleBuffSpellbookController SpellbookController;
        internal bool PendingOpenBuffMode;
        internal int pendingFrameCount;
        internal int pendingPhase; // 0 = waiting for ready, 1 = monitoring party view
        internal int pendingHideFrames;

        internal void ResetPendingState() {
            PendingOpenBuffMode = false;
            pendingFrameCount = 0;
            pendingPhase = 0;
            pendingHideFrames = 0;
        }

        public void OpenBuffMenu() {
            try {
                // If already in buff mode, do nothing (open only, no toggle)
                if (SpellbookController != null && SpellbookController.IsReady && SpellbookController.Buffing)
                    return;

                var serviceWindow = UIHelpers.ServiceWindow;
                var spellScreen = serviceWindow != null ? serviceWindow.Find(UIHelpers.WidgetPaths.SpellScreen) : null;
                bool spellbookVisible = spellScreen != null && spellScreen.gameObject.activeInHierarchy;

                if (spellbookVisible && SpellbookController != null && SpellbookController.IsReady) {
                    SpellbookController.ToggleBuffMode();
                } else {
                    var staticRoot = Game.Instance.UI.Canvas.transform;
                    var spellbookButton = staticRoot.Find("NestedCanvas1/IngameMenuView/ButtonsPart/Container/SpellBookButton")
                        ?.GetComponentInChildren<OwlcatButton>();
                    if (spellbookButton != null) {
                        PendingOpenBuffMode = true;
                        pendingFrameCount = 0;
                        spellbookButton.OnLeftClick.Invoke();
                    } else {
                        Main.Log("BuffIt2TheLimit: Could not find SpellBookButton in IngameMenuView");
                    }
                }
            } catch (Exception ex) {
                Main.Error(ex, "Open Buffs");
            }
        }

        private ButtonSprites applyBuffsSprites;
        private ButtonSprites applyBuffsShortSprites;
        private ButtonSprites showMapSprites;
        private ButtonSprites openBuffsSprites;
        private ButtonSprites applyBuffsImportantSprites;
        private GameObject buttonsContainer;
        public GameObject bubbleHud;
        public GameObject hudLayout;

        public static Sprite[] UnitFrameSprites = new Sprite[2];

        internal static Sprite scrollOverlayIcon;
        internal static Sprite potionOverlayIcon;
        internal static Sprite equipmentOverlayIcon;

        internal static Sprite tabBuffsIcon;
        internal static Sprite tabEquipmentIcon;
        internal static Sprite tabAbilitiesIcon;
        internal static Sprite tabSongsIcon;

        internal static Sprite groupNormalIcon;
        internal static Sprite groupImportantIcon;
        internal static Sprite groupQuickIcon;

        public List<OwlcatButton> Buttons = new();

        public static void TryAddFeature(UnitEntityData u, string feature) {
            var bp = Resources.GetBlueprint<BlueprintFeature>(feature);
            Main.Log("trying to add feature: " + bp.name);
            if (!u.Progression.Features.HasFact(bp)) {
                Main.Log("ADDING");
                u.Progression.Features.AddFeature(bp);
            }
        }

        internal void TryInstallUI() {
            var canvas = Game.Instance.UI.Canvas?.gameObject;
            if (canvas != null && canvas.GetComponent<BubbleBuffGlobalController>() == null) {
                Main.Log("[TryInstallUI] Installing BubbleBuffGlobalController on persistent canvas");
                canvas.AddComponent<BubbleBuffGlobalController>();
            }

            // Compute spellScreen now (null-safe) so SpellbookController can be installed
            // before the gamepad guard — needed for shortcut execution in gamepad mode
            // (e.g. Steam Input mapping controller buttons to keyboard shortcuts).
            var spellScreen = UIHelpers.SpellbookScreen?.gameObject;

            // If SpellbookController was installed on canvas as a gamepad-mode fallback but
            // the spellbook screen is now available, migrate it to the proper host.
            if (SpellbookController != null && spellScreen != null
                    && SpellbookController.gameObject != spellScreen) {
                Main.Verbose("[TryInstallUI] Migrating SpellbookController from canvas to spellbook screen");
                UnityEngine.Object.Destroy(SpellbookController);
                SpellbookController = null;
            }

            if (SpellbookController == null) {
                var host = spellScreen ?? canvas;
                if (host != null) {
                    Main.Verbose($"[TryInstallUI] Installing SpellbookController on {(spellScreen != null ? "spellbook screen" : "canvas (gamepad fallback)")}");
                    SpellbookController = host.AddComponent<BubbleBuffSpellbookController>();
                    SpellbookController.CreateBuffstate();
                }
            }

            if (Game.Instance.IsControllerGamepad) {
                Main.Verbose("[TryInstallUI] Skipping PC HUD install — controller mode is Gamepad");
                return;
            }

            if (spellScreen == null) {
                Main.Verbose("[TryInstallUI] SpellbookScreen not available, cannot install PC HUD");
                return;
            }

            //var u = Game.Instance.Player.ActiveCompanions.First(c => c.CharacterName == "Ember");
            //Main.Log("Got character: " + u.CharacterName);
            //TryAddFeature(u, "2f206e6d292bdfb4d981e99dcf08153f");
            //TryAddFeature(u, "13f9269b3b48ae94c896f0371ce5e23c");

            try {

                //var symbol = Resources.GetBlueprint<BlueprintItemEquipmentUsable>("18f7924f803793a4a9f60495fd88a73b");
                //Game.Instance.Player.MainCharacter.Value.Inventory.Add(symbol);

                Main.Verbose("Installing ui");
                Main.Verbose($"spellscreennull: {spellScreen == null}");

                UnitFrameSprites[0] = AssetLoader.LoadInternal("icons", "UI_HudCharacterFrameBorder_Default.png", new Vector2Int(31, 80));
                UnitFrameSprites[1] = AssetLoader.LoadInternal("icons", "UI_HudCharacterFrameBorder_Hover.png", new Vector2Int(31, 80));

#if DEBUG
                RemoveOldController(spellScreen);
#endif

                Main.Verbose("loading sprites");
                if (applyBuffsSprites == null)
                    applyBuffsSprites = ButtonSprites.Load("apply_buffs", new Vector2Int(95, 95));
                if (applyBuffsShortSprites == null)
                    applyBuffsShortSprites = ButtonSprites.Load("apply_buffs_short", new Vector2Int(95, 95));
                if (applyBuffsImportantSprites == null)
                    applyBuffsImportantSprites = ButtonSprites.Load("apply_buffs_important", new Vector2Int(95, 95));
                if (showMapSprites == null)
                    showMapSprites = ButtonSprites.Load("show_map", new Vector2Int(95, 95));
                if (openBuffsSprites == null)
                    openBuffsSprites = ButtonSprites.Load("open_buffs", new Vector2Int(95, 95));

                if (groupNormalIcon == null)
                    groupNormalIcon = AssetLoader.LoadInternal("icons", "apply_buffs_normal.png", new Vector2Int(24, 24));
                if (groupImportantIcon == null)
                    groupImportantIcon = AssetLoader.LoadInternal("icons", "apply_buffs_important_normal.png", new Vector2Int(24, 24));
                if (groupQuickIcon == null)
                    groupQuickIcon = AssetLoader.LoadInternal("icons", "apply_buffs_short_normal.png", new Vector2Int(24, 24));

                // Load source-type overlay icons from known game blueprints
                if (scrollOverlayIcon == null) {
                    try {
                        var bp = Resources.GetBlueprint<BlueprintItemEquipmentUsable>("be452dba5acdd9441bb6f45f350f1f6b"); // Scroll of Mage Armor
                        if (bp != null) scrollOverlayIcon = bp.Icon;
                    } catch { }
                }
                if (potionOverlayIcon == null) {
                    try {
                        var bp = Resources.GetBlueprint<BlueprintItemEquipmentUsable>("a4093c3baac79f243b8a204e2b1e33e2"); // Potion of Cure Light Wounds
                        if (bp != null) potionOverlayIcon = bp.Icon;
                    } catch { }
                }
                if (equipmentOverlayIcon == null) {
                    try {
                        var bp = Resources.GetBlueprint<BlueprintItemEquipmentUsable>("0e76af02588cad04a8ea5bfebdc9fb40"); // Wand
                        if (bp != null) equipmentOverlayIcon = bp.Icon;
                    } catch { }
                }

                if (tabBuffsIcon == null) {
                    try {
                        var bp = Resources.GetBlueprint<BlueprintAbility>("9e1ad5d6f87d19e4d8c094b114ab2f51"); // Mage Armor
                        if (bp != null) tabBuffsIcon = bp.Icon;
                    } catch { }
                }
                if (tabEquipmentIcon == null) {
                    try {
                        var bp = Resources.GetBlueprint<BlueprintItemEquipmentUsable>("0e76af02588cad04a8ea5bfebdc9fb40"); // Wand
                        if (bp != null) tabEquipmentIcon = bp.Icon;
                    } catch { }
                }
                if (tabAbilitiesIcon == null) {
                    try {
                        var bp = Resources.GetBlueprint<BlueprintAbility>("7bb9eb2042e67bf489c4a7ba8232c6e0"); // Smite Evil
                        if (bp != null) tabAbilitiesIcon = bp.Icon;
                    } catch { }
                }
                if (tabSongsIcon == null) {
                    try {
                        var bp = Resources.GetBlueprint<BlueprintActivatableAbility>("5250c10feed9f8744850fa3b4814e7c0"); // Inspire Courage
                        if (bp != null) tabSongsIcon = bp.Icon;
                    } catch { }
                }

                var staticRoot = Game.Instance.UI.Canvas.transform;
                Main.Verbose("got static root");
                hudLayout = staticRoot.Find("NestedCanvas1/").gameObject;
                Main.Verbose("got hud layout");

                Main.Verbose("Removing old bubble root");
                var oldBubble = hudLayout.transform.parent.Find("BUBBLEMODS_ROOT");
                if (oldBubble != null) {
                    GameObject.Destroy(oldBubble.gameObject);
                }

                bubbleHud = GameObject.Instantiate(hudLayout, hudLayout.transform.parent);
                Main.Verbose("instantiated root");
                bubbleHud.name = "BUBBLEMODS_ROOT";
                var rect = bubbleHud.transform as RectTransform;
                rect.anchoredPosition = new Vector2(0, 96);
                rect.SetSiblingIndex(hudLayout.transform.GetSiblingIndex() + 1);
                Main.Verbose("set sibling index");

                bubbleHud.DestroyComponents<UISectionHUDController>();

                Main.Verbose("destroyed components");

                //GameObject.Destroy(rect.Find("CombatLog_New").gameObject);
                //Main.Verbose("destroyed combatlog_new");

                //GameObject.Destroy(rect.Find("Console_InitiativeTrackerHorizontalPC").gameObject);
                //Main.Verbose("destroyed horizontaltrack");

                GameObject.Destroy(rect.Find("IngameMenuView/CompassPart").gameObject);
                Main.Verbose("destroyed compasspart");

                List<GameObject> toDestroy = new();
                for (int rectChildIndex = 0; rectChildIndex < rect.childCount; rectChildIndex++) {
                    var rectChild = rect.GetChild(rectChildIndex);
                    if (rect.GetChild(rectChildIndex).name != "IngameMenuView")
                        toDestroy.Add(rectChild.gameObject);
                }

                foreach (var obj in toDestroy) {
                    var name = obj.name;
                    GameObject.Destroy(obj);
                }

                bubbleHud.ChildObject("IngameMenuView").DestroyComponents<IngameMenuPCView>();

                Main.Verbose("destroyed old stuff");

                var buttonPanelRect = rect.Find("IngameMenuView/ButtonsPart");
                Main.Verbose("got button panel");
                GameObject.Destroy(buttonPanelRect.Find("TBMMultiButton").gameObject);
                GameObject.Destroy(buttonPanelRect.Find("InventoryButton").gameObject);
                GameObject.Destroy(buttonPanelRect.Find("Background").gameObject);

                Main.Verbose("destroyed more old stuff");

                buttonsContainer = buttonPanelRect.Find("Container").gameObject;
                var buttonsRect = buttonsContainer.transform as RectTransform;
                buttonsRect.anchoredPosition = Vector2.zero;
                buttonsRect.sizeDelta = new Vector2(47.7f * 8, buttonsRect.sizeDelta.y);
                Main.Verbose("set buttons rect");

                buttonsContainer.GetComponent<GridLayoutGroup>().startCorner = GridLayoutGroup.Corner.LowerLeft;

                var prefab = buttonsContainer.transform.GetChild(0).gameObject;
                prefab.SetActive(false);

                int toRemove = buttonsContainer.transform.childCount;

                //Loop from 1 and destroy child[1] since we want to keep child[0] as our prefab, which is super hacky but.
                for (int i = 1; i < toRemove; i++) {
                    GameObject.DestroyImmediate(buttonsContainer.transform.GetChild(1).gameObject);
                }

                void AddButton(string text, string tooltip, ButtonSprites sprites, Action act) {
                    var applyBuffsButton = GameObject.Instantiate(prefab, buttonsContainer.transform);
                    applyBuffsButton.SetActive(true);
                    OwlcatButton button = applyBuffsButton.GetComponentInChildren<OwlcatButton>();
                    button.m_CommonLayer[0].SpriteState = new SpriteState {
                        pressedSprite = sprites.down,
                        highlightedSprite = sprites.hover,
                    };
                    button.OnLeftClick.AddListener(() => {
                        act();
                    });
                    button.SetTooltip(new TooltipTemplateSimple(text, tooltip), new TooltipConfig {
                        InfoCallPCMethod = InfoCallPCMethod.None
                    });

                    Buttons.Add(button);

                    applyBuffsButton.GetComponentInChildren<Image>().sprite = sprites.normal;

                }


                AddButton("group.normal.tooltip.header".i8(), "group.normal.tooltip.desc".i8(), applyBuffsSprites, () => GlobalBubbleBuffer.Execute(BuffGroup.Long));
                AddButton("group.important.tooltip.header".i8(), "group.important.tooltip.desc".i8(), applyBuffsImportantSprites, () => GlobalBubbleBuffer.Execute(BuffGroup.Important));
                AddButton("group.short.tooltip.header".i8(), "group.short.tooltip.desc".i8(), applyBuffsShortSprites, () => GlobalBubbleBuffer.Execute(BuffGroup.Quick));
                if (DungeonController.IsDungeonCampaign) {
                    DungeonShowMap showMap = new();
                    AddButton("showmap.tooltip.header".i8(), "showmap.tooltip.desc".i8(), showMapSprites, () => showMap.RunAction());
                }

                // Add spacer gap before the Open Buffs button
                var spacer = new GameObject("button-spacer", typeof(RectTransform));
                spacer.transform.SetParent(buttonsContainer.transform, false);
                spacer.AddComponent<LayoutElement>().preferredWidth = 20;
                spacer.SetActive(true);

                // Add Open Buffs quick button
                AddButton("openbuffs.tooltip.header".i8(), "openbuffs.tooltip.desc".i8(), openBuffsSprites, () => {
                    OpenBuffMenu();
                });

                Main.Verbose("remove old bubble?");
#if debug
                RemoveOldController<SyncBubbleHud>(hudLayout.ChildObject("IngameMenuView"));
#endif
                var ingameMenuView = hudLayout.ChildObject("IngameMenuView");
                var existingSync = ingameMenuView.GetComponent<SyncBubbleHud>();
                Main.Verbose($"[TryInstallUI] IngameMenuView='{ingameMenuView.name}', existingSyncBubbleHud={existingSync != null}, activeSelf={ingameMenuView.activeSelf}, activeInHierarchy={ingameMenuView.activeInHierarchy}");
                if (existingSync == null) {
                    ingameMenuView.AddComponent<SyncBubbleHud>();
                    Main.Verbose("[TryInstallUI] installed new SyncBubbleHud");
                }



                Main.Verbose("Finished early ui setup");
            } catch (Exception ex) {
                Main.Error(ex, "installing");
            }
        }

#if DEBUG
        private static void RemoveOldController<T>(GameObject on) {
            List<Component> toDelete = new();

            foreach (var component in on.GetComponents<Component>()) {
                Main.Verbose($"checking: {component.name}", "remove-old");
                if (component.GetType().FullName == typeof(T).FullName && component.GetType() != typeof(T)) {
                    var method = component.GetType().GetMethod("Destroy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                    method.Invoke(component, new object[] { });
                    toDelete.Add(component);
                }
                Main.Verbose($"checked: {component.name}", "remove-old");
            }

            int count = toDelete.Count;
            for (int i = 0; i < count; i++) {
                GameObject.Destroy(toDelete[0]);
            }

        }

        private static void RemoveOldController(GameObject spellScreen) {
            RemoveOldController<BubbleBuffSpellbookController>(spellScreen);
            RemoveOldController<BubbleBuffGlobalController>(spellScreen.transform.root.gameObject);
        }
#endif

        internal void SetButtonState(bool v) {
            buttonsContainer?.SetActive(v);
        }

        public static GlobalBubbleBuffer Instance;
        private static ServiceWindowWatcher UiEventSubscriber;
        private static SpellbookWatcher SpellMemorizeHandler;
        private static HideBubbleButtonsWatcher ButtonHiderHandler;

        public static void Install() {

            Instance = new();
            UiEventSubscriber = new();
            SpellMemorizeHandler = new();
            ButtonHiderHandler = new();
            EventBus.Subscribe(Instance);
            EventBus.Subscribe(UiEventSubscriber);
            EventBus.Subscribe(SpellMemorizeHandler);
            EventBus.Subscribe(ButtonHiderHandler);

        }

        public static void Execute(BuffGroup group) {
            Instance.SpellbookController.Execute(group);
        }


        public static void Uninstall() {
            EventBus.Unsubscribe(Instance);
            EventBus.Unsubscribe(UiEventSubscriber);
            EventBus.Unsubscribe(SpellMemorizeHandler);
            EventBus.Unsubscribe(ButtonHiderHandler);
        }
    }


    [Flags]
    public enum HideReason {
        Short = 1,
        Blacklisted = 2,
    };


    public class CasterKey {
        [JsonProperty]
        public string Name;
        [JsonProperty]
        public Guid Spellbook;
        [JsonProperty]
        public BuffSourceType SourceType;

        public override bool Equals(object obj) {
            return obj is CasterKey key &&
                   Name == key.Name &&
                   Spellbook.Equals(key.Spellbook) &&
                   SourceType == key.SourceType;
        }

        public override int GetHashCode() {
            int hashCode = 1282151259;
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(Name);
            hashCode = hashCode * -1521134295 + Spellbook.GetHashCode();
            hashCode = hashCode * -1521134295 + SourceType.GetHashCode();
            return hashCode;
        }
    }
     public enum Category {
        Buff,
        Ability,
        Equipment,
        Song
    }

    public enum BuffGroup {
        Long,
        Quick,
        Important,
    }

    public enum BuffSourceType {
        Spell,
        Scroll,
        Potion,
        Equipment,
        Song
    }

    public enum UmdMode {
        SafeOnly,
        AllowIfPossible,
        AlwaysTry
    }

    public enum SourcePriority {
        SpellsScrollsPotions = 0,
        SpellsPotionsScrolls = 1,
        ScrollsSpellsPotions = 2,
        ScrollsPotionsSpells = 3,
        PotionsSpellsScrolls = 4,
        PotionsScrollsSpells = 5,
    }


    class ButtonSprites {
        public Sprite normal;
        public Sprite hover;
        public Sprite down;

        public static ButtonSprites Load(string name, Vector2Int size) {
            return new ButtonSprites {
                normal = AssetLoader.LoadInternal("icons", $"{name}_normal.png", size),
                hover = AssetLoader.LoadInternal("icons", $"{name}_hover.png", size),
                down = AssetLoader.LoadInternal("icons", $"{name}_down.png", size),
            };
        }
    }


    public static class ReactiveBindings {

        public static void BindToView(this IReactiveProperty<bool> prop, GameObject toggle) {
            var view = toggle.GetComponentInChildren<ToggleWorkaround>();
            prop.Subscribe<bool>(val => {
                if (view.isOn != val) {
                    view.isOn = val;
                }
            });

            view.onValueChanged.AddListener(val => {
                if (prop.Value != val)
                    prop.Value = val;
            });

        }

    }

    public static class Toggles {
        public static Vector3 rotateUp = new Vector3(0, 0, 89.9f);
        public static Vector3 rotateDown = new Vector3(0, 0, -89.9f);
    }
    class Portrait {

        public Image Image;
        public OwlcatButton Button;
        public GameObject GameObject;
        public TextMeshProUGUI Text;
        public OwlcatButton Expand;
        public Image Overlay;
        public Image FullOverlay;
        public Image SourceOverlay;
        public Image SourceOverlayBg;
        public bool State = false;

        public void ExpandOff() {
            SetExpanded(false);
        }

        internal void SetExpanded(bool selected) {
            State = selected;
            //Expand.gameObject.ChildRect("Image").eulerAngles = new Vector3(0, 0, State ? 90 : -90);
            Expand.gameObject.ChildRect("Image").DORotate(State ? Toggles.rotateUp : Toggles.rotateDown, 0.22f).SetUpdate(true);
        }

        public RectTransform Transform { get { return GameObject.transform as RectTransform; } }
    }

    class BubbleCleanup : IDisposable {

        private readonly List<IDisposable> Trash = new();
        public void AddTrash(IDisposable trash) {
            Trash.Add(trash);
        }

        public void Dispose() {
            foreach (var trash in Trash)
                trash.Dispose();
        }

    }

    class BubbleSpellView {
        private static TooltipConfig NoInfo = new TooltipConfig { InfoCallPCMethod = InfoCallPCMethod.None };
        public static void BindBuffToView(BubbleBuff buff, GameObject view, bool tooltipOnRightClickOnly = false) {
            var button = view.GetComponent<OwlcatButton>();
            string text = buff.Name;
            if (buff.IsMass)
                text += " [Mass]";
            //if (buff.Spell.Blueprint.LocalizedDuration.TryGetString(out var duration)) {
            //    text += $"\n<size=70%>{duration}</size>";
            //}
            view.ChildObject("Name/NameLabel").GetComponent<TextMeshProUGUI>().text = text;

            view.ChildObject("Icon/IconImage").GetComponent<Image>().sprite = buff.Icon;
            view.ChildObject("Icon/IconImage").GetComponent<Image>().color = buff.Key.Archmage ? Color.yellow : Color.white;
            view.ChildObject("Icon/FrameImage").GetComponent<Image>().color = buff.Key.Archmage ? Color.yellow : Color.white;

            if (buff.IsSong) {
                view.ChildObject("School/SchoolLabel").GetComponent<TextMeshProUGUI>().text = "";
                view.ChildObject("Metamagic").SetActive(false);
                var songTooltip = new Kingmaker.UI.MVVM._VM.Tooltip.Templates.TooltipTemplateActivatableAbility(buff.ActivatableSource);
                TooltipHelper.SetTooltip(button, songTooltip);
                return;
            }

            if (buff.Spell.Blueprint.School != Kingmaker.Blueprints.Classes.Spells.SpellSchool.None)
                view.ChildObject("School/SchoolLabel").GetComponent<TextMeshProUGUI>().text = buff.Spell.Blueprint.School.ToString();
            else
                view.ChildObject("School/SchoolLabel").GetComponent<TextMeshProUGUI>().text = "";
            var metamagicContainer = view.ChildObject("Metamagic");
            if (buff.Spell.IsMetamagicked()) {
                for (int i = 0; i < metamagicContainer.transform.childCount; i++) {
                    var icon = metamagicContainer.transform.GetChild(i).gameObject;
                    if (i < buff.Metamagics.Length) {
                        icon.SetActive(true);
                        icon.GetComponent<Image>().sprite = buff.Metamagics[i].SpellIcon();
                    } else
                        icon.SetActive(false);
                }
                metamagicContainer.SetActive(true);
            } else
                metamagicContainer.SetActive(false);

            var tooltip = BubbleBuffSpellbookController.TooltipForAbility(buff.Spell.Blueprint);
            
            //button.OnRightClick.RemoveAllListeners();
            //button.OnRightClick.AddListener(() => {
            //    TooltipHelper.ShowInfo(tooltip);
            //});
            TooltipHelper.SetTooltip(button, tooltip);

        }
    }

    class BubbleProfiler : IDisposable {

        private readonly Stopwatch watch = new();
        private readonly string name;


        public BubbleProfiler(string name) {
            this.name = name;
            watch.Start();
        }
        public void Dispose() {
            watch.Stop();
            Main.Verbose($">>> {name} => {watch.Elapsed.TotalSeconds}s");
        }
    }

    class WidgetCache {
        public int Hits;
        public int Misses;
        private GameObject prefab;
        private readonly List<GameObject> cache = new();

        public Func<GameObject> PrefabGenerator;

        public void ResetStats() {
            Hits = 0;
            Misses = 0;
        }

        public WidgetCache() { }

        public GameObject Get(Transform parent) {
            if (prefab == null) {
                prefab = PrefabGenerator.Invoke();
                if (prefab == null)
                    throw new Exception("null prefab in widget cache");
            }
            GameObject ret;
            if (cache.Empty()) {
                ret = GameObject.Instantiate(prefab, parent);
                Misses++;
            } else {
                Hits++;
                ret = cache.Last();
                ret.transform.SetParent(parent);
                cache.RemoveLast();
            }
            ret.SetActive(true);
            return ret;
        }

        public void Return(IEnumerable<GameObject> widgets) {
            //cache.AddRange(widgets);
        }

    }

    class BufferView {
        public Dictionary<BuffKey, GameObject> buffWidgets = new();
        public List<(BuffKey key, string name, int discovery)> DisplayOrder = new();

        public GameObject buffWindow;
        public GameObject removeFromAll;
        public GameObject addToAll;
        public Portrait[] targets;
        private BufferState state;
        public Portrait[] casterPortraits;
        public int[] casterPortraitMap;
        public GameObject castersHolder;
        public TextMeshProUGUI selfCastInfoLabel;

        public GameObject listPrefab;
        public Transform content;
        public Transform leftPanel;

        public WidgetCache widgetCache;

        public BufferView(BufferState state) {
            this.state = state;
            state.OnRecalculated = Update;
        }

        private static GameObject BigLabelPrefab => UIHelpers.CharacterScreen.Find("NamePortrait/CharName/CharacterName").gameObject;

        public void ReorderTargetPortraits() {
            var group = Bubble.Group;
            for (int i = 0; i < group.Count && i < targets.Length; i++) {
                targets[i].Image.sprite = group[i].Portrait.SmallPortrait;
            }
        }

        public void MakeBuffsList() {
            Main.Verbose("here");
            if (!state.Dirty)
                return;
            state.Dirty = false;
            Main.Verbose("state was dirty");

            widgetCache.Return(buffWidgets.Values);
            Main.Verbose("returned widget cache");
            var oldList = content.Find("AvailableBuffList") ?? leftPanel?.Find("AvailableBuffList");
            GameObject.Destroy(oldList?.gameObject);
            Main.Verbose("destroyed old buff list");
            buffWidgets.Clear();
            DisplayOrder.Clear();
            Main.Verbose("cleared widgets");

            var availableBuffs = GameObject.Instantiate(listPrefab.gameObject, leftPanel ?? content);
            availableBuffs.transform.SetAsFirstSibling();
            Main.Verbose("made new buff list");
            availableBuffs.name = "AvailableBuffList";
            availableBuffs.GetComponentInChildren<GridLayoutGroupWorkaround>().constraintCount = 2;
            Main.Verbose("set constraint count");
            var listRect = availableBuffs.transform as RectTransform;
            listRect.localPosition = Vector2.zero;
            listRect.sizeDelta = Vector2.zero;
            listRect.anchorMin = new Vector2(0f, 0.31f);
            listRect.anchorMax = new Vector2(1f, 1f);
            GameObject.Destroy(listRect.Find("Toggle")?.gameObject);
            GameObject.Destroy(listRect.Find("TogglePossibleSpells")?.gameObject);
            GameObject.Destroy(listRect.Find("ToggleAllSpells")?.gameObject);
            GameObject.Destroy(listRect.Find("ToggleMetamagic")?.gameObject);
            var scrollContent = availableBuffs.transform.Find("StandardScrollView/Viewport/Content");
            scrollContent.localScale = new Vector3(0.75f, 0.75f, 0.75f);
            Main.Verbose("got scroll content");
            Main.Verbose($"destroying old stuff: {scrollContent.childCount}");
            int toDestroy = scrollContent.childCount;
            for (int i = 0; i < toDestroy; i++) {
                GameObject.DestroyImmediate(scrollContent.GetChild(0).gameObject);
            }

            Main.Verbose($"destroyed old stuff: {scrollContent.childCount}");
            //widgetListDrawHandle = buffWidgetList.DrawEntries<IWidgetView>(models, new List<IWidgetView> { spellPrefab });

            Color goodSpellColor = new Color(0.2f, 0.7f, 0.2f);

            OwlcatButton previousSelection = null;
            widgetCache.ResetStats();
            using (new BubbleProfiler("making widgets")) {
                foreach (var buff in state.BuffList) {
                    GameObject widget = widgetCache.Get(scrollContent);
                    var button = widget.GetComponent<OwlcatButton>();
                    button.OnHover.RemoveAllListeners();
                    button.OnHover.AddListener(hover => {
                        PreviewReceivers(hover ? buff : null);
                    });
                    button.OnSingleLeftClick.AddListener(() => {
                        if (previousSelection != null && previousSelection != button) {
                            previousSelection.IsPressed = false;
                        }
                        if (!button.IsPressed) {
                            button.IsPressed = true;
                        }
                        currentSelectedSpell.Value = buff;
                        previousSelection = button;
                    });
                    var label = widget.ChildObject("School/SchoolLabel").GetComponent<TextMeshProUGUI>();
                    var textImage = widget.ChildObject("Name/BackgroundName").GetComponent<Image>();
                    buff.OnUpdate = () => {
                        if (widget == null)
                            return;
                        var (availNormal, availSelf) = buff.AvailableAndSelfOnly;
                        if (availNormal < 100)
                            label.text = $"{"casting".i8()}: {buff.Fulfilled}/{buff.Requested} + {"available".i8()}: {availNormal}+{availSelf}";
                        else
                            label.text = $"{"casting".i8()}: {buff.Fulfilled}/{buff.Requested} + {"available".i8()}: {"available.atwill".i8()}";
                        if (buff.Requested > 0) {
                            if (buff.Fulfilled != buff.Requested) {
                                textImage.color = Color.red;
                            } else {
                                textImage.color = goodSpellColor;
                            }

                        } else {
                            textImage.color = Color.white;
                        }
                    };
                    BubbleSpellView.BindBuffToView(buff, widget, button);
                    widget.ChildObject("School").SetActive(true);
                    widget.SetActive(true);

                    DisplayOrder.Add((buff.Key, buff.Name, DisplayOrder.Count));
                    buffWidgets[buff.Key] = widget;
                }
            }

            Main.Verbose($"Widget cache: created={widgetCache.Hits + widgetCache.Misses}");

            foreach (var buff in state.BuffList) {
                buff.OnUpdate();
            }
        }

        public void Update() {
            if (state.Dirty) {
                try {
                    MakeBuffsList();
                    ReorderTargetPortraits();
                } catch (Exception ex) {
                    Main.Error(ex, "revalidating dirty");
                }
            }

            foreach (BuffGroup group in Enum.GetValues(typeof(BuffGroup))) {
                try {
                    if (groupSummaryLabels.TryGetValue(group, out var label)) {
                        var list = state.BuffList.Where(b => b.InGroups.Contains(group))
                                                                   .Select(b => (b.Requested, b.Fulfilled));
                        if (!list.Empty()) {
                            var (requested, fulfilled) = list.Aggregate((a, b) => (a.Requested + b.Requested, a.Fulfilled + b.Fulfilled));
                            label.text = MakeSummaryLabel(group, requested, fulfilled);
                        } else {
                            label.text = MakeSummaryLabel(group, 0, 0);
                        }
                    }
                } catch (Exception e) {
                    Main.Error(e, "");
                }
            }

            if (currentSelectedSpell.Value == null)
                return;

            PreviewReceivers(null);
            UpdateCasterDetails(Selected);
            OnUpdate?.Invoke();
        }

        private string MakeSummaryLabel(BuffGroup group, int requested, int fulfilled) {
             return $"{group.i8().MakeTitle()}\n{fulfilled}/{requested}";
        }


        public Action OnUpdate;

        Color massGoodColor = new Color(0, 1, 0, 0.4f);
        Color massBadColor = new Color(1, 1, 0, 0.4f);

        public void PreviewReceivers(BubbleBuff buff) {
            if (buff == null && currentSelectedSpell.Value != null)
                buff = Selected;

            for (int p = 0; p < Bubble.Group.Count && p < targets.Length; p++)
                UpdateTargetBuffColor(buff, p);
        }

        private void UpdateTargetBuffColor(BubbleBuff buff, int i) {
            var fullOverlay = targets[i].FullOverlay;
            targets[i].Button.Interactable = true;
            if (buff == null) {
                fullOverlay.gameObject.SetActive(false);
                return;
            }
            bool isMass = false;
            bool massGood = false;

            if (buff.IsMass && buff.Requested > 0) {
                isMass = true;
                if (buff.Fulfilled > 0)
                    massGood = true;
            }

            var me = Bubble.Group[i];


            if (isMass && !buff.UnitWants(me)) {
                var target = massGood ? massGoodColor : massBadColor;
                targets[i].Overlay.gameObject.SetActive(true);
                var current = targets[i].Overlay.color;
                targets[i].Overlay.color = new Color(target.r, target.g, target.b, current.a);
            } else {
                targets[i].Overlay.gameObject.SetActive(false);
            }

            fullOverlay.gameObject.SetActive(true);

            if (!buff.CanTarget(me)) {
                fullOverlay.color = Color.red;
                targets[i].Button.Interactable = false;

            } else if (buff.UnitWants(me)) {
                if (buff.UnitGiven(me)) {
                    fullOverlay.color = Color.green;
                } else {
                    fullOverlay.color = Color.yellow;
                }
            } else {
                fullOverlay.color = Color.gray;
            }
        }

        private string BuildCasterTooltip(BubbleBuff buff, string casterId) {
            var lines = new List<string>();

            foreach (var provider in buff.CasterQueue) {
                if (provider.who.UniqueId != casterId) continue;

                if (lines.Count > 0) lines.Add("");

                switch (provider.SourceType) {
                    case BuffSourceType.Spell:
                        var spellLevel = provider.spell?.SpellLevel ?? 0;
                        var bookName = provider.book?.Blueprint.Name ?? "";
                        lines.Add(string.Format("tooltip.source.spell".i8(), bookName, spellLevel));
                        if (provider.AvailableCredits < 100)
                            lines.Add(string.Format("tooltip.source.stacks".i8(), provider.AvailableCredits));
                        break;
                    case BuffSourceType.Scroll:
                        var scrollName = provider.SourceItem?.Blueprint.Name ?? provider.spell?.Blueprint.Name ?? "";
                        lines.Add(string.Format("tooltip.source.scroll".i8(), scrollName));
                        lines.Add(string.Format("tooltip.source.stacks".i8(), provider.AvailableCredits));
                        break;
                    case BuffSourceType.Potion:
                        var potionName = provider.SourceItem?.Blueprint.Name ?? provider.spell?.Blueprint.Name ?? "";
                        lines.Add(string.Format("tooltip.source.potion".i8(), potionName));
                        lines.Add(string.Format("tooltip.source.stacks".i8(), provider.AvailableCredits));
                        break;
                    case BuffSourceType.Equipment:
                        var equipName = provider.SourceItem?.Blueprint.Name ?? provider.spell?.Blueprint.Name ?? "";
                        lines.Add(string.Format("tooltip.source.equipment".i8(), equipName));
                        lines.Add(string.Format("tooltip.source.charges".i8(), provider.AvailableCredits));
                        break;
                    default:
                        continue;
                }

                // UMD hint for scroll/wand sources not on class spell list
                if (provider.SourceType == BuffSourceType.Scroll || provider.SourceType == BuffSourceType.Equipment) {
                    bool onClassList = provider.who.Spellbooks.Any(b =>
                        b.Blueprint.SpellList?.SpellsByLevel?.Any(level =>
                            level.Spells.Any(s => s == provider.spell?.Blueprint)) == true);
                    if (!onClassList) {
                        lines.Add(string.Format("tooltip.source.umd".i8(), provider.ScrollDC));
                    }
                }
            }

            return string.Join("\n", lines);
        }

        private void UpdateCasterDetails(BubbleBuff buff) {
            var seen = new HashSet<string>();
            var distinctCasters = new List<int>();

            for (int i = 0; i < buff.CasterQueue.Count; i++) {
                var provider = buff.CasterQueue[i];
                if (seen.Add(provider.who.UniqueId)) {
                    bool hasNonSelfOnly = false;
                    for (int j = i; j < buff.CasterQueue.Count; j++) {
                        if (buff.CasterQueue[j].who == provider.who && !buff.CasterQueue[j].SelfCastOnly) {
                            hasNonSelfOnly = true;
                            break;
                        }
                    }
                    if (hasNonSelfOnly)
                        distinctCasters.Add(i);
                }
            }

            casterPortraitMap = distinctCasters.ToArray();

            bool isSelfOnly = distinctCasters.Count == 0 && buff.CasterQueue.Count > 0;
            castersHolder.SetActive(!isSelfOnly);
            selfCastInfoLabel?.gameObject.SetActive(isSelfOnly);

            for (int i = 0; i < casterPortraits.Length; i++) {
                casterPortraits[i].GameObject.SetActive(i < distinctCasters.Count);
                if (i < distinctCasters.Count) {
                    var who = buff.CasterQueue[distinctCasters[i]];
                    if (who.CharacterIndex < targets.Length)
                        casterPortraits[i].Image.sprite = targets[who.CharacterIndex].Image.sprite;
                    var summaryParts = new List<string>();
                    foreach (var p in buff.CasterQueue) {
                        if (p.who.UniqueId != who.who.UniqueId) continue;
                        string abbr = p.SourceType switch {
                            BuffSourceType.Spell => "Sp",
                            BuffSourceType.Scroll => "Sc",
                            BuffSourceType.Potion => "Po",
                            BuffSourceType.Equipment => "Eq",
                            _ => null
                        };
                        if (abbr != null) {
                            summaryParts.Add(p.AvailableCredits < 100 ? $"{abbr}:{p.AvailableCredits}" : abbr);
                        }
                    }
                    casterPortraits[i].Text.text = string.Join("\n", summaryParts);
                    if (casterPortraits[i].SourceOverlay != null) {
                        if (who.SourceType == BuffSourceType.Spell) {
                            casterPortraits[i].SourceOverlay.gameObject.SetActive(false);
                            casterPortraits[i].SourceOverlayBg?.gameObject.SetActive(false);
                        } else {
                            var overlaySprite = who.SourceType switch {
                                BuffSourceType.Scroll => GlobalBubbleBuffer.scrollOverlayIcon,
                                BuffSourceType.Potion => GlobalBubbleBuffer.potionOverlayIcon,
                                BuffSourceType.Equipment => GlobalBubbleBuffer.equipmentOverlayIcon,
                                _ => null
                            };
                            if (overlaySprite != null) {
                                casterPortraits[i].SourceOverlay.sprite = overlaySprite;
                                casterPortraits[i].SourceOverlay.gameObject.SetActive(true);
                                casterPortraits[i].SourceOverlayBg?.gameObject.SetActive(true);
                            } else {
                                casterPortraits[i].SourceOverlay.gameObject.SetActive(false);
                                casterPortraits[i].SourceOverlayBg?.gameObject.SetActive(false);
                            }
                        }
                    }
                    // Bind hover tooltip
                    if (who.SourceType == BuffSourceType.Song && buff.ActivatableSource != null) {
                        var songTooltip = new Kingmaker.UI.MVVM._VM.Tooltip.Templates.TooltipTemplateActivatableAbility(buff.ActivatableSource);
                        casterPortraits[i].Button.SetTooltip(songTooltip, new TooltipConfig {
                            InfoCallPCMethod = InfoCallPCMethod.None
                        });
                    } else {
                        var tooltipBody = BuildCasterTooltip(buff, who.who.UniqueId);
                        casterPortraits[i].Button.SetTooltip(
                            new TooltipTemplateSimple(who.who.CharacterName, tooltipBody),
                            new TooltipConfig { InfoCallPCMethod = InfoCallPCMethod.None });
                    }
                    casterPortraits[i].Text.fontSize = 14;
                    casterPortraits[i].Text.lineSpacing = 4;
                    casterPortraits[i].Text.outlineWidth = 0;
                    casterPortraits[i].Image.color = who.Banned ? Color.red : Color.white;
                }
            }
            addToAll.GetComponentInChildren<OwlcatButton>().Interactable = buff.Requested != Bubble.Group.Count;
            removeFromAll.GetComponentInChildren<OwlcatButton>().Interactable = buff.Requested > 0;
        }

        public IReactiveProperty<BubbleBuff> currentSelectedSpell = new ReactiveProperty<BubbleBuff>();

        public bool Get(out BubbleBuff buff) {
            buff = currentSelectedSpell.Value;
            if (currentSelectedSpell.Value == null)
                return false;
            return true;
        }

        private Dictionary<BuffGroup, TextMeshProUGUI> groupSummaryLabels = new();

        internal void MakeSummary() {
            groupSummaryLabels.Clear();
            var rect = new GameObject("summary", typeof(RectTransform));
            rect.AddTo(content);
            rect.Rect().sizeDelta = Vector3.zero;

            rect.MakeComponent<GridLayoutGroupWorkaround>(h => {
                h.constraint = GridLayoutGroup.Constraint.FixedRowCount;
                h.constraintCount = 1;
                h.childAlignment = TextAnchor.MiddleCenter;
                h.cellSize = new Vector2(400, 100);
            });

            foreach (BuffGroup group in Enum.GetValues(typeof(BuffGroup))) {
                var l = GameObject.Instantiate(BigLabelPrefab, rect.transform);
                var label = l.GetComponent<TextMeshProUGUI>();
                label.text = MakeSummaryLabel(group, 0, 0);
                l.SetActive(true);
                groupSummaryLabels[group] = label;
            }
            rect.Rect().SetAnchor(0.05, 0.95, 0.90, 0.97);

        }

        public BubbleBuff Selected {
            get {
                if (currentSelectedSpell == null)
                    return null;
                return currentSelectedSpell.Value;
            }
        }
    }

    static class Bubble {
        public static List<UnitEntityData> Group = new();
        public static List<UnitEntityData> ConfigGroup = new();
        public static Dictionary<string, UnitEntityData> GroupById = new();
        public static bool ShowReserve = false;

        public static void RefreshGroup() {
            var baseGroup = Game.Instance.SelectionCharacter.ActualGroup;
            var result = new List<UnitEntityData>(baseGroup);

            foreach (var unit in baseGroup) {
                var petMaster = unit.Get<UnitPartPetMaster>();
                if (petMaster == null) continue;

                var pets = new List<UnitEntityData>();
                foreach (var petRef in petMaster.Pets) {
                    var pet = petRef.Entity;
                    if (pet != null && pet.IsInGame && !result.Contains(pet)) {
                        pets.Add(pet);
                    }
                }
                pets.Sort((a, b) => string.Compare(a.UniqueId, b.UniqueId, StringComparison.Ordinal));
                result.AddRange(pets);
            }

            Group = result;

            if (ShowReserve) {
                var config = new List<UnitEntityData>(result);
                var activeIds = new HashSet<string>(result.Select(u => u.UniqueId));

                foreach (var unit in Game.Instance.Player.AllCharacters) {
                    if (activeIds.Contains(unit.UniqueId)) continue;

                    config.Add(unit);

                    var petMaster = unit.Get<UnitPartPetMaster>();
                    if (petMaster == null) continue;

                    var pets = new List<UnitEntityData>();
                    foreach (var petRef in petMaster.Pets) {
                        var pet = petRef.Entity;
                        if (pet != null && !activeIds.Contains(pet.UniqueId) && !config.Contains(pet)) {
                            pets.Add(pet);
                        }
                    }
                    pets.Sort((a, b) => string.Compare(a.UniqueId, b.UniqueId, StringComparison.Ordinal));
                    config.AddRange(pets);
                }
                ConfigGroup = config;
            } else {
                ConfigGroup = result;
            }

            GroupById.Clear();
            foreach (var u in ConfigGroup) {
                GroupById[u.UniqueId] = u;
            }
        }
    }


    internal class SpellbookWatcher : ISpellBookUIHandler, IAreaHandler, ILevelUpCompleteUIHandler, IPartyChangedUIHandler, ISpellBookCustomSpell, IAreaActivationHandler {
        public static void Safely(Action a) {
            try {
                a.Invoke();
            } catch (Exception ex) {
                Main.Error(ex, "");
            }
        }
        public void HandleForgetSpell(AbilityData data, UnitDescriptor owner) {
            Safely(() => GlobalBubbleBuffer.Instance.SpellbookController?.RevalidateSpells());
        }

        public void HandleLevelUpComplete(UnitEntityData unit, bool isChargen) {
            Safely(() => GlobalBubbleBuffer.Instance.SpellbookController?.RevalidateSpells());
        }

        public void HandleMemorizedSpell(AbilityData data, UnitDescriptor owner) {
            Safely(() => GlobalBubbleBuffer.Instance.SpellbookController?.RevalidateSpells());
        }

        public void HandlePartyChanged() {
            Safely(() => GlobalBubbleBuffer.Instance.SpellbookController?.RevalidateSpells());
        }


        public void OnAreaDidLoad() {
            //Main.Log("Loaded area...");
            //GlobalBubbleBuffer.Instance.TryInstallUI();
            //AbilityCache.Revalidate();

        }

        public void OnAreaActivated() {
            Main.Verbose("Loaded area...");
            GlobalBubbleBuffer.Instance.TryInstallUI();
            AbilityCache.Revalidate();


        }

        public void OnAreaBeginUnloading() { }

        void ISpellBookCustomSpell.AddSpellHandler(AbilityData ability) {
            Safely(() => GlobalBubbleBuffer.Instance.SpellbookController?.RevalidateSpells());
        }

        void ISpellBookCustomSpell.RemoveSpellHandler(AbilityData ability) {
            Safely(() => GlobalBubbleBuffer.Instance.SpellbookController?.RevalidateSpells());
        }
    }
    internal class HideBubbleButtonsWatcher : ICutsceneHandler, IPartyCombatHandler {
        public void HandleCutscenePaused(CutscenePlayerData cutscene, CutscenePauseReason reason) { }

        public void HandleCutsceneRestarted(CutscenePlayerData cutscene) { }

        public void HandleCutsceneResumed(CutscenePlayerData cutscene) { }

        public void HandleCutsceneStarted(CutscenePlayerData cutscene, bool queued) { }

        public void HandleCutsceneStopped(CutscenePlayerData cutscene) { }

        public void HandlePartyCombatStateChanged(bool inCombat) {
            bool allow = !inCombat || GlobalBubbleBuffer.Instance.SpellbookController.state.AllowInCombat;
            GlobalBubbleBuffer.Instance.Buttons.ForEach(b => b.Interactable = allow);
        }
    }

    static class BubbleBlueprints {
        public static BlueprintAbility ShareTransmutation => Resources.GetBlueprint<BlueprintAbility>("749567e4f652852469316f787921e156");
        public static BlueprintAbility PowerfulChange => Resources.GetBlueprint<BlueprintAbility>("a45f3dae9c64ec848b35f85568f4b220");
        public static BlueprintAbility ReservoirBaseAbility => Resources.GetBlueprint<BlueprintAbility>("91295893ae9fdfb4b8936a93eff019df");
        public static BlueprintArchetype PhantasmalMageArchetype => Resources.GetBlueprint<BlueprintArchetype>("e9d0ee69305049fe8400a066010dbcd1");
    }
}
