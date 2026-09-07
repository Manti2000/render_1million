using MillionObjects.Benchmark;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace MillionObjects
{
    /// <summary>
    /// Start screen of the bootstrap scene: pick a step and an object count, or launch the automatic
    /// benchmark or recording script. Escape brings it back and unloads the active step.
    /// </summary>
    public class StartMenu : MonoBehaviour
    {
        #region Constants
        private static readonly int[] CountChoices = { 1_000, 10_000, 100_000, 1_000_000 };
        #endregion

        #region Inspector fields
        [SerializeField] private UIDocument _document;
        [SerializeField] private BackendSwitcher _switcher;
        [SerializeField] private BenchmarkRunner _runner;
        [SerializeField] private RecordingDirector _director;
        [SerializeField] private Hud _hud;
        #endregion

        #region Public properties
        /// <summary>Whether the menu is on screen.</summary>
        public bool Visible
        {
            get => _root != null && _root.style.display != DisplayStyle.None;
            private set { if (_root != null) _root.style.display = value ? DisplayStyle.Flex : DisplayStyle.None; }
        }
        #endregion

        #region Private fields
        private VisualElement _root;
        private VisualElement _countButtons;
        private Button _back;                  // corner button shown while a step runs by hand
        private Label _status;
        private int _selectedCount = 10_000;   // count applied when a step is started from the menu
        private bool _automaticRunActive;      // benchmark or recording in progress; Escape is ignored
        #endregion

        #region Lifecycle
        private void Awake()
        {
            _switcher.SuppressAutoLoad();
            DisplayMode.UncapFrameRate();
        }

        private void OnEnable()
        {
            _root = _document.rootVisualElement.Q<VisualElement>("menu");
            _status = _root.Q<Label>("status");
            _back = _document.rootVisualElement.Q<Button>("back");
            _back.clicked += Show;
            BuildStepButtons(_root.Q<VisualElement>("steps"));
            BuildCountButtons(_root.Q<VisualElement>("counts"));
            BindAutomaticButtons();
            if (_runner != null)
                _runner.RunCompleted += OnBenchmarkCompleted;
        }

        private void Start()
        {
            Show();   // after every OnEnable, so the HUD exists to be hidden
        }

        private void OnDisable()
        {
            if (_runner != null)
                _runner.RunCompleted -= OnBenchmarkCompleted;
        }

        private void Update()
        {
            if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame)
                return;
            if (_automaticRunActive || Visible || _switcher.IsLoading)
                return;
            Show();
        }
        #endregion

        #region Public interface
        /// <summary>Shows the menu, unloads the active step and hides the HUD and back button.</summary>
        public void Show()
        {
            Visible = true;
            ShowBackButton(false);
            if (_hud != null)
                _hud.Visible = false;
            if (_switcher.Active != null)
                _switcher.UnloadStep();
        }
        #endregion

        #region Building
        /// <summary>One button per catalog step.</summary>
        private void BuildStepButtons(VisualElement container)
        {
            container.Clear();
            for (int i = 0; i < _switcher.Catalog.Steps.Count; i++)
            {
                int stepIndex = i;
                var button = new Button(() => StartStep(stepIndex)) { text = _switcher.Catalog.Steps[i].DisplayName };
                button.AddToClassList("button");
                button.AddToClassList("step");
                container.Add(button);
            }
        }

        /// <summary>Count choices as a single-select button row.</summary>
        private void BuildCountButtons(VisualElement container)
        {
            _countButtons = container;
            container.Clear();
            foreach (int count in CountChoices)
            {
                int choice = count;
                var button = new Button(() => SelectCount(choice)) { text = choice.ToString("N0") };
                button.AddToClassList("button");
                button.AddToClassList("count");
                button.EnableInClassList("selected", choice == _selectedCount);
                button.userData = choice;
                container.Add(button);
            }
        }

        /// <summary>Wires the benchmark and recording buttons.</summary>
        private void BindAutomaticButtons()
        {
            _root.Q<Button>("benchmark-full").clicked += () => StartBenchmark(false);
            _root.Q<Button>("benchmark-quick").clicked += () => StartBenchmark(true);
            _root.Q<Button>("record").clicked += StartRecording;
        }
        #endregion

        #region Actions
        /// <summary>Hides the menu and loads a step with the selected count.</summary>
        private void StartStep(int stepIndex)
        {
            if (_switcher.IsLoading)
                return;
            Visible = false;
            ShowBackButton(true);
            if (_hud != null)
                _hud.Visible = true;
            _switcher.SpawnCount = _selectedCount;
            _switcher.LoadStep(stepIndex, true);
        }

        /// <summary>Remembers the count and updates the selected button.</summary>
        private void SelectCount(int count)
        {
            _selectedCount = count;
            foreach (var child in _countButtons.Children())
                child.EnableInClassList("selected", child.userData is int value && value == count);
        }

        /// <summary>Starts the unattended benchmark; the menu returns with the report path when it finishes.</summary>
        private void StartBenchmark(bool quick)
        {
            if (_runner == null || _switcher.IsLoading)
                return;
            _automaticRunActive = true;
            Visible = false;
            if (_hud != null)
                _hud.Visible = true;
            _status.text = string.Empty;
            _runner.StartRun(BenchmarkArgs.ForManualRun(quick), false);
        }

        /// <summary>Plays the recording script once, then quits the player or exits play mode.</summary>
        private void StartRecording()
        {
            if (_director == null || _switcher.IsLoading)
                return;
            _automaticRunActive = true;
            Visible = false;
            if (_hud != null)
                _hud.Visible = true;
            _director.StartRun(BenchmarkArgs.ForManualRun(false));
        }

        /// <summary>Toggles the corner back button; hidden in the menu and during automatic runs.</summary>
        private void ShowBackButton(bool visible)
        {
            if (_back != null)
                _back.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Returns to the menu and shows where the report landed.</summary>
        private void OnBenchmarkCompleted(string reportPath)
        {
            _automaticRunActive = false;
            if (_hud != null)
                _hud.SetMinimal(false);
            Show();
            _status.text = reportPath != null ? $"Report written to {reportPath}" : "Report could not be written.";
        }
        #endregion
    }
}
