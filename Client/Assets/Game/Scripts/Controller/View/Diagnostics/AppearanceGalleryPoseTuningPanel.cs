using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game
{
    /// <summary>手机微调的展示与按压适配；只提交相对手的握持参数，不改变角色动画或装备状态。</summary>
    public sealed class AppearanceGalleryPoseTuningPanel : MonoBehaviour
    {
        public event System.Action<bool> TuningModeChanged;
        [SerializeField] private GameObject browsePage, tuningPage, valuesPage, transferPage, morePage;
        /// <summary>左侧镜头手势提示；仅浏览页显示，避免与微调加减提示混在一起。</summary>
        [SerializeField] private GameObject previewGestureHint;
        [SerializeField] private Button browseButton, tuningButton;
        [SerializeField] private Dropdown scopeDropdown;
        // 保留隐藏的旧步长控件作为选择值，用户直接点选细、中、粗。
        [SerializeField] private Dropdown stepDropdown;
        [SerializeField] private Button fineButton, mediumButton, coarseButton;
        [SerializeField] private Button positionButton, rotationButton, scaleButton;
        // 原数值控件只读且不接受射线，不唤起手机键盘。
        [SerializeField] private Text xValue, yValue, zValue;
        [SerializeField] private Text xAxisLabel, yAxisLabel, zAxisLabel;
        [SerializeField] private Button xMinus, xPlus, yMinus, yPlus, zMinus, zPlus;
        [SerializeField] private Button resetButton, resetPartButton, saveButton, undoButton;
        [SerializeField] private Button copyPreviousButton, copyButton, pastePoseButton;
        [SerializeField] private Button axisScaleButton, moreBackButton, importExportButton;
        [SerializeField] private Text copySourceText;
        [SerializeField] private Button transferButton, exportButton, pasteButton, importButton, backButton;
        [SerializeField] private InputField transferInput;
        [SerializeField] private Text statusText, partText;
        [SerializeField] private Transform axisGuide;
        private WeaponPoseSettingsSystem settings;
        private CharacterWeaponCatalog catalog;
        private CharacterWeaponCatalog.Entry weapon;
        private CharacterAnimationDefinition animation;
        private WeaponPosePart part;
        private int revision = -1;
        private bool independentScale, resetArmed, wired;
        private Vector3 rotationDeltas;
        private AppearanceGalleryAdjustButton heldButton;
        private int heldAxis, heldDirection;
        private float repeatRemaining;
        private LineRenderer[] axisLines;
        // 剪贴板只含局部握持，不保存手骨骼世界坐标或动画采样状态。
        private sealed class GripCopy
        {
            public string WeaponId, Source;
            public Vector3 Position, Scale;
            public Quaternion Rotation;
        }
        private GripCopy previous, clipboard;
        private CharacterAnimationDefinition Scope => scopeDropdown.value == 0 ? animation : null;
        private bool CanEdit => weapon != null && (scopeDropdown.value != 0 || animation != null);
        public bool IsAdjusting => heldButton != null;

        private void Awake()
        {
            if (wired) return;
            wired = true;
            axisLines = axisGuide.GetComponentsInChildren<LineRenderer>(true);
            browseButton.onClick.AddListener(() => ShowTuning(false));
            tuningButton.onClick.AddListener(() => ShowTuning(true));
            scopeDropdown.onValueChanged.AddListener(_ => { CancelHold(); rotationDeltas = Vector3.zero; Refresh(); });
            positionButton.onClick.AddListener(() => SelectPart(WeaponPosePart.Position));
            rotationButton.onClick.AddListener(() => SelectPart(WeaponPosePart.Rotation));
            scaleButton.onClick.AddListener(() => { independentScale = false; SelectPart(WeaponPosePart.Scale); });
            fineButton.onClick.AddListener(() => SetStep(0));
            mediumButton.onClick.AddListener(() => SetStep(1));
            coarseButton.onClick.AddListener(() => SetStep(2));
            BindAdjust(xMinus, 0, -1); BindAdjust(xPlus, 0, 1);
            BindAdjust(yMinus, 1, -1); BindAdjust(yPlus, 1, 1);
            BindAdjust(zMinus, 2, -1); BindAdjust(zPlus, 2, 1);
            undoButton.onClick.AddListener(() => { CancelHold(); settings.Undo(); rotationDeltas = Vector3.zero; Refresh(); });
            copyPreviousButton.onClick.AddListener(() => ApplyCopy(previous));
            copyButton.onClick.AddListener(() => { CancelHold(); if (CanEdit) clipboard = Capture(Scope); Refresh(); });
            pastePoseButton.onClick.AddListener(() => ApplyCopy(clipboard));
            axisScaleButton.onClick.AddListener(() => { independentScale = true; ShowValues(); SelectPart(WeaponPosePart.Scale); });
            resetPartButton.onClick.AddListener(() => { CancelHold(); if (!CanEdit) return; settings.ResetPart(weapon.Id, Scope, part); rotationDeltas = Vector3.zero; Refresh(); });
            resetButton.onClick.AddListener(() =>
            {
                CancelHold(); if (!CanEdit) return;
                if (!resetArmed) { resetArmed = true; statusText.text = "将恢复" + ScopeName() + "的全部握持参数；再次点击确认。"; return; }
                settings.Reset(weapon.Id, Scope); rotationDeltas = Vector3.zero; Refresh();
            });
            saveButton.onClick.AddListener(() => { CancelHold(); statusText.text = settings.Save(out var error) ? "已保存到本机。" : "保存失败：" + error; });
            transferButton.onClick.AddListener(() => { CancelHold(); valuesPage.SetActive(false); morePage.SetActive(true); Refresh(); });
            moreBackButton.onClick.AddListener(ShowValues);
            importExportButton.onClick.AddListener(() => { CancelHold(); morePage.SetActive(false); transferPage.SetActive(true); });
            backButton.onClick.AddListener(() => { transferPage.SetActive(false); morePage.SetActive(true); Refresh(); });
            exportButton.onClick.AddListener(() => { transferInput.text = settings.Export(); GUIUtility.systemCopyBuffer = transferInput.text; statusText.text = "已复制整份本机配置（包含未保存调整）。"; });
            pasteButton.onClick.AddListener(() => transferInput.text = GUIUtility.systemCopyBuffer);
            importButton.onClick.AddListener(() =>
            {
                CancelHold();
                if (settings.Import(transferInput.text, catalog, out var error)) { rotationDeltas = Vector3.zero; Refresh(); statusText.text = "导入成功，可撤销；点击保存写入本机。"; }
                else statusText.text = "导入失败：" + error;
            });
            tuningButton.interactable = false;
        }
        private void BindAdjust(Button button, int axis, int direction)
        {
            var press = (AppearanceGalleryAdjustButton)button;
            press.PressStarted += () => BeginHold(press, axis, direction);
            press.PressEnded += CancelHold;
            button.onClick.AddListener(() => { CancelHold(); Nudge(axis, direction); });
        }
        /// <summary>场景加载后绑定唯一姿态服务，不自行启动架构。</summary>
        public void Initialize(WeaponPoseSettingsSystem system, CharacterWeaponCatalog weapons)
        {
            settings = system; catalog = weapons; SelectPart(WeaponPosePart.Position); ShowTuning(false);
            if (!string.IsNullOrEmpty(system.LoadWarning)) statusText.text = system.LoadWarning;
        }
        /// <summary>切换动作前捕获旧动作的局部握持；仅刷新版本时不打断长按。</summary>
        public void Bind(CharacterWeaponCatalog.Entry selectedWeapon, CharacterAnimationDefinition selectedAnimation)
        {
            if (settings == null) return;
            bool changed = weapon != selectedWeapon || animation != selectedAnimation;
            if (!changed && revision == settings.Revision) return;
            if (changed)
            {
                CancelHold();
                if (weapon == selectedWeapon && animation != null && animation != selectedAnimation) previous = Capture(animation);
                else if (weapon != selectedWeapon) previous = null;
                rotationDeltas = Vector3.zero;
            }
            weapon = selectedWeapon; animation = selectedAnimation; Refresh();
        }
        private void ShowTuning(bool show)
        {
            CancelHold(); EventSystem.current?.SetSelectedGameObject(null); TuningModeChanged?.Invoke(show);
            browsePage.SetActive(!show); tuningPage.SetActive(show);
            previewGestureHint.SetActive(!show);
            browseButton.interactable = show; tuningButton.interactable = !show;
            ShowValues(); if (settings != null) Refresh();
        }
        private void ShowValues()
        {
            CancelHold(); EventSystem.current?.SetSelectedGameObject(null);
            valuesPage.SetActive(true); transferPage.SetActive(false); morePage.SetActive(false);
            if (settings != null) Refresh();
        }
        private void SelectPart(WeaponPosePart selected) { CancelHold(); part = selected; stepDropdown.SetValueWithoutNotify(0); if (settings != null) Refresh(); }
        private void SetStep(int index) { CancelHold(); stepDropdown.SetValueWithoutNotify(index); Refresh(); }
        private string ScopeName() => Scope == null ? "武器默认姿态" : "动作“" + AnimationName(Scope) + "”";
        private static string AnimationName(CharacterAnimationDefinition value) => AppearanceGallerySceneView.GetAnimationDefinitionDisplayName(value);
        private GripCopy Capture(CharacterAnimationDefinition source)
        {
            settings.Resolve(weapon, source, out var p, out var r, out var s);
            return new GripCopy { WeaponId = weapon.Id, Source = source == null ? "武器默认姿态" : AnimationName(source), Position = p, Rotation = r, Scale = s };
        }
        private bool CanPaste(GripCopy copy) => CanEdit && copy != null && copy.WeaponId == weapon.Id;
        private void ApplyCopy(GripCopy copy)
        {
            CancelHold(); if (!CanPaste(copy)) return;
            if (!settings.SetPose(weapon, Scope, copy.Position, copy.Rotation, copy.Scale, out var error)) { statusText.text = error; return; }
            rotationDeltas = Vector3.zero; Refresh();
            statusText.text = "已复制“" + copy.Source + "”握持参数到" + ScopeName() + "；可撤销，角色动画保持独立。";
        }
        private void Refresh()
        {
            revision = settings.Revision; resetArmed = false;
            xMinus.interactable = xPlus.interactable = yMinus.interactable = yPlus.interactable = zMinus.interactable = zPlus.interactable = resetButton.interactable = resetPartButton.interactable = CanEdit;
            positionButton.interactable = part != WeaponPosePart.Position;
            rotationButton.interactable = part != WeaponPosePart.Rotation;
            scaleButton.interactable = part != WeaponPosePart.Scale || independentScale;
            fineButton.interactable = stepDropdown.value != 0; mediumButton.interactable = stepDropdown.value != 1; coarseButton.interactable = stepDropdown.value != 2;
            undoButton.interactable = settings.CanUndo;
            copyPreviousButton.interactable = scopeDropdown.value == 0 && CanPaste(previous);
            copyButton.interactable = CanEdit; pastePoseButton.interactable = CanPaste(clipboard); axisScaleButton.interactable = CanEdit;
            partText.text = part == WeaponPosePart.Position ? "相对手骨骼 · 厘米" : part == WeaponPosePart.Rotation ? "武器自身轴 · 本次累计增量（度）" : independentScale ? "各轴大小 · 默认值百分比" : "整体大小 · 保持原有比例";
            bool uniform = part == WeaponPosePart.Scale && !independentScale;
            SetAxisVisible(yValue, yMinus, yPlus, yAxisLabel, !uniform); SetAxisVisible(zValue, zMinus, zPlus, zAxisLabel, !uniform);
            xAxisLabel.text = uniform ? "大小" : part == WeaponPosePart.Rotation ? "X 轴" : "X";
            yAxisLabel.text = part == WeaponPosePart.Rotation && weapon?.Id == "weapon.short-sword" ? "剑身滚转 · Y" : part == WeaponPosePart.Rotation ? "Y 轴" : "Y";
            zAxisLabel.text = part == WeaponPosePart.Rotation ? "Z 轴" : "Z";
            SetButtonText(fineButton, part == WeaponPosePart.Position ? "细 0.1cm" : part == WeaponPosePart.Rotation ? "细 0.1°" : "细 0.1%");
            SetButtonText(mediumButton, part == WeaponPosePart.Position ? "中 1cm" : part == WeaponPosePart.Rotation ? "中 1°" : "中 1%");
            SetButtonText(coarseButton, part == WeaponPosePart.Position ? "粗 10cm" : part == WeaponPosePart.Rotation ? "粗 5°" : "粗 5%");
            SetButtonText(resetPartButton, "恢复当前" + (part == WeaponPosePart.Position ? "位置" : part == WeaponPosePart.Rotation ? "旋转" : "大小"));
            copySourceText.text = previous == null ? "切换动作后可复制上一动作的握持参数" : "来源：“" + previous.Source + "” → " + ScopeName();
            if (CanEdit)
            {
                settings.Resolve(weapon, Scope, out var p, out _, out var s);
                var d = weapon.HandLocalScale;
                var v = part == WeaponPosePart.Position ? p * 100f : part == WeaponPosePart.Rotation ? rotationDeltas : new Vector3(s.x / d.x, s.y / d.y, s.z / d.z) * 100f;
                xValue.text = Format(v.x); yValue.text = Format(v.y); zValue.text = Format(v.z);
                statusText.text = settings.HasUnsavedChanges ? "有未保存调整 · 保存后重启继续生效" : "已保存或使用默认握持参数";
            }
            else { xValue.text = yValue.text = zValue.text = ""; statusText.text = "请先选择武器和动作。"; }
            if (part != WeaponPosePart.Rotation || !tuningPage.activeSelf || !valuesPage.activeSelf) axisGuide.gameObject.SetActive(false);
        }
        private string Format(float value) => value.ToString("0.##", CultureInfo.InvariantCulture) + (part == WeaponPosePart.Position ? " cm" : part == WeaponPosePart.Rotation ? "°" : "%");
        private static void SetButtonText(Button button, string value) => button.GetComponentInChildren<Text>(true).text = value;
        private static void SetAxisVisible(Text input, Button minus, Button plus, Text label, bool visible) { input.transform.parent.gameObject.SetActive(visible); minus.gameObject.SetActive(visible); plus.gameObject.SetActive(visible); label.gameObject.SetActive(visible); }
        private void BeginHold(AppearanceGalleryAdjustButton button, int axis, int direction)
        {
            if (heldButton != null && heldButton != button) { button.CancelPress(); return; }
            CancelHold();
            if (!CanEdit || !tuningPage.activeSelf || !valuesPage.activeSelf) { button.CancelPress(); return; }
            heldButton = button; heldAxis = axis; heldDirection = direction; repeatRemaining = .35f;
            settings.BeginEditGroup(); Nudge(axis, direction);
        }
        /// <summary>由既有场景生命周期传入非缩放时间；卡顿后不追赶积压次数，避免突然跳值。</summary>
        public void Tick(float unscaledDeltaTime)
        {
            if (heldButton == null) return;
            if (!heldButton.IsPressed || !heldButton.IsInteractable() || !tuningPage.activeInHierarchy || !valuesPage.activeInHierarchy) { CancelHold(); return; }
            repeatRemaining -= Mathf.Max(0, unscaledDeltaTime);
            if (repeatRemaining > 0f) return;
            repeatRemaining = .08f; Nudge(heldAxis, heldDirection);
        }
        /// <summary>松开、切换目标或应用失焦时结束分组；先清空拥有者避免取消事件重入。</summary>
        public void CancelHold()
        {
            var b = heldButton; heldButton = null; b?.CancelPress(); settings?.EndEditGroup();
        }
        private void Nudge(int axis, int direction)
        {
            if (!CanEdit) return;
            settings.Resolve(weapon, Scope, out var p, out _, out var s);
            int i = stepDropdown.value;
            float step = part == WeaponPosePart.Position ? (i == 0 ? .001f : i == 1 ? .01f : .1f) : part == WeaponPosePart.Rotation ? (i == 0 ? .1f : i == 1 ? 1f : 5f) : (i == 0 ? .001f : i == 1 ? .01f : .05f);
            bool success; string error;
            if (part == WeaponPosePart.Rotation) { success = settings.RotateLocal(weapon, Scope, axis, step * direction, out error); if (success) rotationDeltas[axis] += step * direction; }
            else if (part == WeaponPosePart.Position) { p[axis] += step * direction; success = settings.Set(weapon, Scope, part, p, out error); }
            else
            {
                if (independentScale) s[axis] = Mathf.Max(weapon.HandLocalScale[axis] * .01f, s[axis] + weapon.HandLocalScale[axis] * step * direction);
                else { float ratio = s.x / weapon.HandLocalScale.x; float next = Mathf.Max(.01f, ratio + step * direction); s *= next / ratio; }
                success = settings.Set(weapon, Scope, part, s, out error);
            }
            if (!success) { CancelHold(); statusText.text = error; return; }
            Refresh();
        }
        /// <summary>动画采样后同步武器自身轴；纯展示对象不参与碰撞、装备或存档。</summary>
        public void RefreshAxisGuide(Transform target)
        {
            bool show = target != null && tuningPage.activeInHierarchy && valuesPage.activeInHierarchy && part == WeaponPosePart.Rotation;
            axisGuide.gameObject.SetActive(show); if (show) axisGuide.SetPositionAndRotation(target.position, target.rotation);
            if (show)
                for (int i = 0; i < axisLines.Length; i++)
                    axisLines[i].startWidth = axisLines[i].endWidth = IsAdjusting && heldAxis == i ? .03f : .012f;
        }
        private void OnApplicationFocus(bool focused) { if (!focused) CancelHold(); }
        private void OnApplicationPause(bool paused) { if (paused) CancelHold(); }
        private void OnDisable()
        {
            CancelHold(); if (axisGuide != null) axisGuide.gameObject.SetActive(false);
            var e = EventSystem.current;
            if (e != null && e.currentSelectedGameObject != null && e.currentSelectedGameObject.transform.IsChildOf(transform)) e.SetSelectedGameObject(null);
        }
    }
}
