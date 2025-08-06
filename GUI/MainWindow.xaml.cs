using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using GARbro.GUI.Properties;
using GARbro.GUI.Strings;
using GARbro.GUI.Preview;
using GameRes;
using Rnd.Windows;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace GARbro.GUI
{
    public enum MediaType
    {
        None,
        Audio,
        Video
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private App m_app;
        public App App { get { return m_app; } }

        public static readonly GuiResourceSetting DownScaleImage = new GuiResourceSetting ("winDownScaleImage");

        const StringComparison StringIgnoreCase = StringComparison.CurrentCultureIgnoreCase;

        private bool _isShuttingDown = false;
        private readonly List<IDisposable> _activeStreams = new List<IDisposable>();

        private ImagePreviewHandler _imagePreviewHandler;
        private AudioPreviewHandler _audioPreviewHandler;
        private VideoPreviewHandler _videoPreviewHandler;
        private  TextPreviewHandler _textPreviewHandler;
        private        MediaControl _mediaControl;

        internal bool _isAutoPlaying = false;
        internal bool _isAutoCycling = false;

        internal ImagePreviewControl m_animated_image_viewer;
        private VideoPreviewControl  m_video_preview_ctl;
        private               string m_video_base_info = "";

        public MainWindow()
        {
            m_app = Application.Current as App;
            InitializeComponent();

            if (this.Top < 0) this.Top = 0;
            if (this.Left < 0) this.Left = 0;

            InitDirectoryChangesWatcher();
            InitPreviewPane();
            //InitUpdatesChecker();
            InitializeMediaControls();
            LoadEncodingHistory();

            FitWindowMenuItem.IsChecked = DownScaleImage.Get<bool>();

            if (null == Settings.Default.appRecentFiles)
                Settings.Default.appRecentFiles = new StringCollection();
            m_recent_files = new LinkedList<string> (Settings.Default.appRecentFiles.Cast<string>().Take (MaxRecentFiles));
            RecentFilesMenu.ItemsSource = RecentFiles;

            FormatCatalog.Instance.ParametersRequest += (s, e) => Dispatcher.Invoke (() => OnParametersRequest (s, e));

            CurrentDirectory.SizeChanged += (s, e) => {
                if (e.WidthChanged)
                {
                    pathLine.MinWidth = e.NewSize.Width - 79;
                    this.MinWidth = e.NewSize.Width + 79;
                }
            };

            DownScaleImage.PropertyChanged += (s, e) => {
                ApplyDownScaleSetting();
                FitWindowMenuItem.IsChecked = DownScaleImage.Get<bool>();
            };
            pathLine.EnterKeyDown += acb_OnKeyDown;

            this.Closing += OnClosing;
        }

        /// <summary>
        /// Initialize media control system using existing XAML elements.
        /// </summary>
        private void InitializeMediaControls ()
        {
            _mediaControl = new MediaControl (
                MediaControlPanel,
                MediaPauseButton,
                MediaStopButton,
                MediaCycleButton,
                MediaAutoButton,
                MediaVolumeSlider
            );

            _mediaControl.ConfigureForMediaType (MediaType.None);
        }

        private MediaType _currentMediaType = MediaType.None;

        /// <summary>
        /// Update media controls visibility based on current media type.
        /// </summary>
        internal void UpdateMediaControlsVisibility (MediaType mediaType)
        {
            _currentMediaType = mediaType;
            Dispatcher.Invoke(() => _mediaControl.ConfigureForMediaType (mediaType));
        }

        /// <summary>
        /// Clean up resources when window is closing.
        /// </summary>
        private void OnClosing (object sender, CancelEventArgs e)
        {
            _isShuttingDown = true;
            try
            {
                StopAudioPlayback();
                StopVideoPlayback();
                StopAnimationPlayback();

                //SaveSettings();
                DisposeAllStreams();
                DisposePreviewHandlers();
            }
            catch (Exception X)
            {
                Trace.WriteLine (X.Message, "[OnClosing]");
                Trace.WriteLine (X.StackTrace, "Stack trace");
            }
        }

        /// <summary>
        /// Dispose all preview handlers.
        /// </summary>
        private void DisposePreviewHandlers ()
        {
            _imagePreviewHandler?.Dispose();
            _audioPreviewHandler?.Dispose();
            _videoPreviewHandler?.Dispose();
            _textPreviewHandler?.Dispose();
        }

        void WindowLoaded (object sender, RoutedEventArgs e)
        {
            lv_SetSortMode (Settings.Default.lvSortColumn, Settings.Default.lvSortDirection);
            Dispatcher.InvokeAsync (WindowRendered, DispatcherPriority.ContextIdle);
            ImageData.SetDefaultDpi (Desktop.DpiX, Desktop.DpiY);
        }

        void WindowRendered ()
        {
            DirectoryViewModel vm = null;
            try
            {
                vm = GetNewViewModel (m_app.InitPath);
            }
            catch (Exception X)
            {
                PopupError (X.Message, guiStrings.MsgErrorOpening);
            }
            if (null == vm)
            {
                vm = CreateViewModel (Directory.GetCurrentDirectory(), true);
            }
            ViewModel = vm;
            lv_SelectItem (0);
            if (!vm.IsArchive)
                SetFileStatus (guiStrings.MsgReady);
        }

        void WindowKeyDown (object sender, KeyEventArgs e)
        {
            if (MainMenuBar.Visibility != Visibility.Visible && Key.System == e.Key)
            {
                MainMenuBar.Visibility = Visibility.Visible;
                MainMenuBar.IsKeyboardFocusWithinChanged += HideMenuBar;
            }
        }

        void HideMenuBar (object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!MainMenuBar.IsKeyboardFocusWithin)
            {
                MainMenuBar.IsKeyboardFocusWithinChanged -= HideMenuBar;
                MainMenuBar.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Manually save settings that are not automatically saved by bindings.
        /// </summary>
        private void SaveSettings ()
        {
            if (null != m_lvSortByColumn)
            {
                Settings.Default.lvSortColumn = SortMode;
                Settings.Default.lvSortDirection = m_lvSortDirection;
            }
            else
                Settings.Default.lvSortColumn = "";

            Settings.Default.appRecentFiles.Clear();
            foreach (var file in m_recent_files)
                Settings.Default.appRecentFiles.Add (file);

            string cwd = CurrentPath;
            if (!string.IsNullOrEmpty (cwd))
            {
                if (ViewModel.IsArchive)
                    cwd = Path.GetDirectoryName (cwd);
            }
            else
                cwd = Directory.GetCurrentDirectory();
            Settings.Default.appLastDirectory = cwd;
        }

        /// <summary>
        /// Set preview status line text.<br/> 
        /// Could be called from any thread.
        /// </summary>
        public void SetFileStatus (string text)
        {
            Dispatcher.Invoke(() => { appFileStatus.Text = text.Trim(); });
        }

        /// <summary>
        /// Set directoty listing status line text.<br/> 
        /// Could be called from any thread.
        /// </summary>
        public void SetPreviewStatus (string text)
        {
            Dispatcher.Invoke(() => { appPreviewStatus.Text = text.Trim(); });
        }

        /// <summary>
        /// Popup error message box.<br/> 
        /// Could be called from any thread.
        /// </summary>
        public void PopupError (string message, string title)
        {
            Dispatcher.Invoke(() => MessageBox.Show (this, message, title, MessageBoxButton.OK, MessageBoxImage.Error));
        }

        internal FileErrorDialogResult ShowErrorDialog (string error_title, string error_text, IntPtr parent_hwnd)
        {
            var dialog = new FileErrorDialog (error_title, error_text);
            SetModalWindowParent (dialog, parent_hwnd);
            return dialog.ShowDialog();
        }

        internal FileExistsDialogResult ShowFileExistsDialog (string title, string text, IntPtr parent_hwnd)
        {
            var dialog = new FileExistsDialog (title, text);
            SetModalWindowParent (dialog, parent_hwnd);
            return dialog.ShowDialog();
        }

        private void SetModalWindowParent (Window dialog, IntPtr parent_hwnd)
        {
            if (parent_hwnd != IntPtr.Zero)
            {
                var native_dialog = new WindowInteropHelper (dialog);
                native_dialog.Owner = parent_hwnd;
                NativeMethods.EnableWindow (parent_hwnd, false);
                EventHandler on_closed = null;
                on_closed = (s, e) => {
                    NativeMethods.EnableWindow (parent_hwnd, true);
                    dialog.Closed -= on_closed;
                };
                dialog.Closed += on_closed;
            }
            else
            {
                dialog.Owner = this;
            }
        }

        const int MaxRecentFiles = 9;
        LinkedList<string> m_recent_files;

        // Item1 = file name, Item2 = menu item string
        public IEnumerable<Tuple<string, string>> RecentFiles
        {
            get
            {
                int i = 1;
                return m_recent_files.Select (f => Tuple.Create (f, string.Format("_{0} {1}", i++, f)));
            }
        }

        void PushRecentFile (string file)
        {
            var node = m_recent_files.Find (file);
            if (node != null && node == m_recent_files.First)
                return;
            if (null == node)
            {
                while (MaxRecentFiles <= m_recent_files.Count)
                    m_recent_files.RemoveLast();
                m_recent_files.AddFirst (file);
            }
            else
            {
                m_recent_files.Remove (node);
                m_recent_files.AddFirst (node);
            }
            RecentFilesMenu.ItemsSource = RecentFiles;
        }

        /// <summary>
        /// Data context of the <see cref="ListView"/>
        /// </summary>
        public DirectoryViewModel ViewModel
        {
            get
            {
                var source = CurrentDirectory.ItemsSource as CollectionView;
                if (null == source)
                    return null;
                return source.SourceCollection as DirectoryViewModel;
            }
            private set
            {
                StopWatchDirectoryChanges();
                var cvs = this.Resources["ListViewSource"] as CollectionViewSource;
                cvs.Source = value;

                // update path textbox
                var path_component = value.Path.Last();
                if (string.IsNullOrEmpty (path_component) && value.Path.Count > 1)
                    path_component = value.Path[value.Path.Count - 2];
                pathLine.Text = path_component;

                if (value.IsArchive && value.Path.Count <= 2)
                    PushRecentFile (value.Path.First());

                lv_Sort (SortMode, m_lvSortDirection);
                if (!value.IsArchive && !string.IsNullOrEmpty (value.Path.First()))
                {
                    WatchDirectoryChanges (value.Path.First());
                }
                CurrentDirectory.UpdateLayout();
            }
        }

        /// <summary>
        /// Save current position and update view model.
        /// </summary>
        void PushViewModel (DirectoryViewModel vm)
        {
            SaveCurrentPosition();
            ViewModel = vm;
        }

        DirectoryViewModel GetNewViewModel (string path)
        {
            if (!string.IsNullOrEmpty (path))
            {
                if (!VFS.IsVirtual)
                    path = Path.GetFullPath (path);
                var entry = VFS.FindFile (path);
                if (!(entry is SubDirEntry))
                    SetBusyState();
                VFS.ChDir (entry);
            }
            return new DirectoryViewModel (VFS.FullPath, VFS.GetFiles(), VFS.IsVirtual);
        }

        private bool m_busy_state = false;

        public void SetBusyState()
        {
            m_busy_state = true;
            Mouse.OverrideCursor = Cursors.Wait;
            Dispatcher.InvokeAsync(() => {
                m_busy_state = false;
                Mouse.OverrideCursor = null;
            }, DispatcherPriority.ApplicationIdle);
        }

        /// <summary>
        /// Create view model corresponding to <paramref name="path">.
        /// Returns <b>null</b> on error.
        /// </summary>
        DirectoryViewModel TryCreateViewModel (string path)
        {
            try
            {
                return GetNewViewModel (path);
            }
            catch (Exception X)
            {
                SetFileStatus (string.Format("{0}: {1}", Path.GetFileName (path), X.Message));
                return null;
            }
        }

        /// <summary>
        /// Create view model corresponding to <paramref name="path">
        /// or empty view model if there was an error accessing path.
        /// </summary>
        DirectoryViewModel CreateViewModel (string path, bool suppress_warning = false)
        {
            try
            {
                return GetNewViewModel (path);
            }
            catch (Exception X)
            {
                if (!suppress_warning)
                    PopupError (X.Message, guiStrings.MsgErrorOpening);
                return new DirectoryViewModel (new string[] { "" }, new Entry[0], false);
            }
        }

        #region Refresh view on filesystem changes

        private FileSystemWatcher m_watcher = new FileSystemWatcher();

        void InitDirectoryChangesWatcher ()
        {
            m_watcher.NotifyFilter = NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.DirectoryName;
            m_watcher.Changed += InvokeRefreshView;
            m_watcher.Created += InvokeRefreshView;
            m_watcher.Deleted += InvokeRefreshView;
            m_watcher.Renamed += InvokeRefreshView;
        }

        void WatchDirectoryChanges (string path)
        {
            m_watcher.Path = path;
            m_watcher.EnableRaisingEvents = true;
        }

        public void StopWatchDirectoryChanges()
        {
            m_watcher.EnableRaisingEvents = false;
        }

        public void ResumeWatchDirectoryChanges ()
        {
            m_watcher.EnableRaisingEvents = !ViewModel.IsArchive;
        }

        private void InvokeRefreshView (object source, FileSystemEventArgs e)
        {
            var watcher = source as FileSystemWatcher;
            var vm = ViewModel;
            if (!vm.IsArchive && vm.Path.First() == watcher.Path)
            {
                watcher.EnableRaisingEvents = false;
                Dispatcher.Invoke (RefreshView);
            }
        }
        #endregion

        /// <summary>
        /// Select specified item within CurrentDirectory and bring it into a view.
        /// </summary>
        void lv_SelectItem (EntryViewModel item)
        {
            if (item != null)
            {
                CurrentDirectory.SelectedItem = item;
                CurrentDirectory.ScrollIntoView (item);
                var lvi = (ListViewItem)CurrentDirectory.ItemContainerGenerator.ContainerFromItem (item);
                if (lvi != null)
                    lvi.Focus();
            }
        }

        void lv_SelectItem (int index)
        {
            CurrentDirectory.SelectedIndex = index;
            CurrentDirectory.ScrollIntoView (CurrentDirectory.SelectedItem);
            var lvi = (ListViewItem)CurrentDirectory.ItemContainerGenerator.ContainerFromIndex (index);
            if (lvi != null)
                lvi.Focus();
        }

        void lv_SelectItem (string name)
        {
            if (!string.IsNullOrEmpty (name))
                lv_SelectItem (ViewModel.Find (name));
        }

        public void ListViewFocus ()
        {
            if (CurrentDirectory.SelectedIndex != -1)
            {
                var item = CurrentDirectory.SelectedItem;
                var lvi = CurrentDirectory.ItemContainerGenerator.ContainerFromItem (item) as ListViewItem;
                if (lvi != null)
                {
                    lvi.Focus();
                    return;
                }
            }
            CurrentDirectory.Focus();
        }

        void lvi_Selected (object sender, RoutedEventArgs args)
        {
            var lvi = sender as ListViewItem;
            if (lvi == null)
                return;
            var entry = lvi.Content as EntryViewModel;
            if (entry == null)
                return;
            PreviewEntry (entry.Source);
        }

        EntryViewModel m_last_selected = null;

        void lv_SelectionChanged (object sender, SelectionChangedEventArgs args)
        {
            var lv = sender as ListView;
            if (null == lv)
                return;
            var item = lv.SelectedItem as EntryViewModel;
            if (item != null && m_last_selected != item)
            {
                m_last_selected = item;
                _textPreviewHandler.Reset();
                PreviewEntry (item.Source);
            }
        }

        void lvi_DoubleClick (object sender, MouseButtonEventArgs args)
        {
            var lvi = sender as ListViewItem;
            if (Commands.OpenItem.CanExecute (null, lvi))
            {
                Commands.OpenItem.Execute (null, lvi);
                args.Handled = true;
            }
        }

        /// <summary>
        /// Get currently selected item from ListView widget.
        /// </summary>
        private ListViewItem lv_GetCurrentContainer ()
        {
            int current = CurrentDirectory.SelectedIndex;
            if (-1 == current)
                return null;

            return CurrentDirectory.ItemContainerGenerator.ContainerFromIndex (current) as ListViewItem;
        }

        GridViewColumnHeader m_lvSortByColumn = null;
        ListSortDirection m_lvSortDirection = ListSortDirection.Ascending;

        public string SortMode
        {
            get { return GetValue (SortModeProperty) as string; }
            private set { SetValue (SortModeProperty, value); }
        }

        public static readonly DependencyProperty SortModeProperty =
            DependencyProperty.RegisterAttached ("SortMode", typeof(string), typeof(MainWindow), new UIPropertyMetadata());

        void lv_SetSortMode (string sortBy, ListSortDirection direction)
        {
            m_lvSortByColumn = null;
            GridView view = CurrentDirectory.View as GridView;
            foreach (var column in view.Columns)
            {
                var header = column.Header as GridViewColumnHeader;
                if (null != header && !string.IsNullOrEmpty (sortBy) && sortBy.Equals (header.Tag))
                {
                    if (ListSortDirection.Ascending == direction)
                        column.HeaderTemplate = Resources["SortArrowUp"] as DataTemplate;
                    else
                        column.HeaderTemplate = Resources["SortArrowDown"] as DataTemplate;
                    m_lvSortByColumn = header;
                    m_lvSortDirection = direction;
                }
                else
                {
                    column.HeaderTemplate = Resources["SortArrowNone"] as DataTemplate;
                }
            }
            SortMode = sortBy;
        }

        private void lv_Sort (string sortBy, ListSortDirection direction)
        {
            var dataView = CollectionViewSource.GetDefaultView (CurrentDirectory.ItemsSource) as ListCollectionView;
            dataView.CustomSort = new FileSystemComparer (sortBy, direction);
        }

        /// <summary>
        /// Sort Listview by columns.
        /// </summary>
        void lv_ColumnHeaderClicked (object sender, RoutedEventArgs e)
        {
            var headerClicked = e.OriginalSource as GridViewColumnHeader;

            if (null == headerClicked)
                return;
            if (headerClicked.Role == GridViewColumnHeaderRole.Padding)
                return;

            ListSortDirection direction;
            if (headerClicked != m_lvSortByColumn)
                direction = ListSortDirection.Ascending;
            else if (m_lvSortDirection == ListSortDirection.Ascending)
                direction = ListSortDirection.Descending;
            else
                direction = ListSortDirection.Ascending;

            string sortBy = headerClicked.Tag.ToString();
            lv_Sort (sortBy, direction);
            SortMode = sortBy;

            // Remove arrow from previously sorted header 
            if (m_lvSortByColumn != null && m_lvSortByColumn != headerClicked)
                m_lvSortByColumn.Column.HeaderTemplate = Resources["SortArrowNone"] as DataTemplate;

            if (ListSortDirection.Ascending == direction)
                headerClicked.Column.HeaderTemplate = Resources["SortArrowUp"] as DataTemplate;
            else
                headerClicked.Column.HeaderTemplate = Resources["SortArrowDown"] as DataTemplate;

            m_lvSortByColumn = headerClicked;
            m_lvSortDirection = direction;
        }

        /// <summary>
        /// Handle "Sort By" commands.
        /// </summary>
        private void SortByExec (object sender, ExecutedRoutedEventArgs e)
        {
            string sort_by = e.Parameter as string;
            lv_Sort (sort_by, ListSortDirection.Ascending);
            lv_SetSortMode (sort_by, ListSortDirection.Ascending);
        }

        /// <summary>
        /// Handle "Set file type" commands.
        /// </summary>
        private void SetFileTypeExec (object sender, ExecutedRoutedEventArgs e)
        {
            var selected = CurrentDirectory.SelectedItems.Cast<EntryViewModel>().Where (x => !x.IsDirectory);
            if (!selected.Any())
                return;
            string type = e.Parameter as string;
            foreach (var entry in selected)
            {
                entry.Type = type;
            }
        }

        /// <summary>
        /// Event handler for keys pressed in the directory view pane.
        /// </summary>
        private void lv_TextInput (object sender, TextCompositionEventArgs e)
        {
            LookupItem (e.Text, e.Timestamp);
            e.Handled = true;
        }

        private void lv_KeyDown (object sender, KeyEventArgs e)
        {
            if (e.IsDown && LookupActive)
            {
                switch (e.Key)
                {
                case Key.Space:
                    LookupItem (" ", e.Timestamp);
                    e.Handled = true;
                    break;
                case Key.Down:
                case Key.Up:
                case Key.Left:
                case Key.Right:
                case Key.Next:
                case Key.Prior:
                case Key.Home:
                case Key.End:
                case Key.Enter:
                    m_current_input.Reset();
                    break;
                }
            }
        }

        class InputData
        {
            public int              LastTime = 0;
            public StringBuilder    Phrase = new StringBuilder();
            public bool             Mismatch = false;

            public bool LookupActive
            {
                get { return Phrase.Length > 0 && Environment.TickCount - LastTime < TextLookupTimeout; }
            }

            public void Reset ()
            {
                Phrase.Clear ();
                Mismatch = false;
            }

            public void Update (int timestamp)
            {
                if (timestamp - LastTime >= TextLookupTimeout)
                {
                    Reset();
                }
                LastTime = timestamp;
            }
        }

        const int TextLookupTimeout = 1000; // milliseconds

        InputData m_current_input = new InputData();

        public bool LookupActive { get { return m_current_input.LookupActive; } }

        /// <summary>
        /// Lookup item in listview pane by first letters of name.
        /// </summary>
        private void LookupItem (string key, int timestamp)
        {
            if (string.IsNullOrEmpty (key))
                return;
            if ("\x1B" == key) // Esc key
            {
                m_current_input.Reset();
                return;
            }
            var source = CurrentDirectory.ItemsSource as CollectionView;
            if (source == null)
                return;

            m_current_input.Update (timestamp);
            if (m_current_input.Mismatch)
                return;

            if (!(1 == m_current_input.Phrase.Length && m_current_input.Phrase[0] == key[0]))
            {
                m_current_input.Phrase.Append (key);
            }
            int start_index = CurrentDirectory.SelectedIndex;
            if (1 == m_current_input.Phrase.Length)
            {
                // lookup starting from the next item
                if (start_index != -1 && start_index + 1 < source.Count)
                    ++start_index;
            }
            var items = source.Cast<EntryViewModel>();
            if (start_index > 0)
            {
                items = items.Skip (start_index).Concat (items.Take (start_index));
            }
            string input = m_current_input.Phrase.ToString();
            var matched = items.FirstOrDefault (e => e.Name.StartsWith (input, StringIgnoreCase));
            if (null != matched)
                lv_SelectItem (matched);
            else
                m_current_input.Mismatch = true;
        }

        static readonly Regex FullpathRe = new Regex (@"^(?:[a-z]:|[\\/])", RegexOptions.IgnoreCase);

        private void acb_OnKeyDown (object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Return)
                return;
            string path = (sender as AutoCompleteBox).Text;
            path = path.Trim (' ', '"');
            if (string.IsNullOrEmpty (path))
                return;
            if (FullpathRe.IsMatch (path))
            {
                OpenFile (path);
                return;
            }
            try
            {
                PushViewModel (GetNewViewModel (path));
                ListViewFocus();
            }
            catch (Exception X)
            {
                PopupError (X.Message, guiStrings.MsgErrorOpening);
            }
        }

        #region Navigation history implementation

        internal string CurrentPath { get { return ViewModel.Path.First(); } }

        HistoryStack<DirectoryPosition> m_history = new HistoryStack<DirectoryPosition>();

        public DirectoryPosition GetCurrentPosition ()
        {
            var evm = CurrentDirectory.SelectedItem as EntryViewModel;
            return new DirectoryPosition (ViewModel, evm);
        }

        public bool SetCurrentPosition (DirectoryPosition pos)
        {
            try
            {
                VFS.FullPath = pos.Path;
                var vm = TryCreateViewModel (pos.Path.Last());
                if (null == vm)
                    return false;
                ViewModel = vm;
                if (null != pos.Item)
                    lv_SelectItem (pos.Item);
                return true;
            }
            catch (Exception X)
            {
                // if VFS.FullPath throws an exception, ViewModel becomes
                // inconsistent at this point and should be rebuilt
                ViewModel = CreateViewModel (VFS.Top.CurrentDirectory, true);
                SetFileStatus (X.Message);
                return false;
            }
        }

        public void SaveCurrentPosition ()
        {
            m_history.Push (GetCurrentPosition());
        }

        public void ChangePosition (DirectoryPosition new_pos)
        {
            var current = GetCurrentPosition();
            if (!current.Path.SequenceEqual (new_pos.Path))
                SaveCurrentPosition();
            SetCurrentPosition (new_pos);
        }

        private void GoBackExec (object sender, ExecutedRoutedEventArgs e)
        {
            DirectoryPosition current = m_history.Undo (GetCurrentPosition());
            if (current != null)
                SetCurrentPosition (current);
        }

        private void GoForwardExec (object sender, ExecutedRoutedEventArgs e)
        {
            DirectoryPosition current = m_history.Redo (GetCurrentPosition());
            if (current != null)
                SetCurrentPosition (current);
        }

        private void CanExecuteGoBack (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = m_history.CanUndo();
        }

        private void CanExecuteGoForward (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = m_history.CanRedo();
        }
        #endregion

        private void OpenFileExec (object control, ExecutedRoutedEventArgs e)
        {
            var dlg = new OpenFileDialog {
                CheckFileExists = true,
                CheckPathExists = true,
                Multiselect = false,
                Title = guiStrings.TextChooseArchive,
            };
            if (!dlg.ShowDialog (this).Value)
                return;
            OpenFile (dlg.FileName);
        }

        private void OpenFile (string filename)
        {
            try
            {
                OpenFileOrDir (filename);
            }
            catch (OperationCanceledException X)
            {
                SetFileStatus (X.Message);
            }
            catch (Exception X)
            {
                PopupError (string.Format("{0}\n{1}", filename, X.Message), guiStrings.MsgErrorOpening);
            }
        }

        private void OpenFileOrDir (string filename)
        {
            if (filename == CurrentPath || string.IsNullOrEmpty (filename))
                return;
            if (File.Exists (filename))
                VFS.FullPath = new string[] { filename, "" };
            else
                VFS.FullPath = new string[] { filename };
            var vm = new DirectoryViewModel (VFS.FullPath, VFS.GetFiles(), VFS.IsVirtual);
            PushViewModel (vm);
            ShowCurrentArchiveStatus();
            lv_SelectItem (0);
        }

        private void ShowCurrentArchiveStatus()
        {
            if (null == VFS.CurrentArchive)
                return;

            SetFileStatus(VFS.CurrentArchive.Description);
            string comment = "";
            if (!string.IsNullOrEmpty(VFS.CurrentArchive.Comment))
                comment = VFS.CurrentArchive.Comment + ": ";
            comment += VFS.CurrentArchive.Dir.Count().Pluralize("MsgFiles");
            SetPreviewStatus(comment);
        }

        private void OpenRecentExec (object control, ExecutedRoutedEventArgs e)
        {
            string filename = e.Parameter as string;
            if (string.IsNullOrEmpty (filename))
                return;
            OpenFile (filename);
        }

        /// <summary>
        /// Open file/directory.
        /// </summary>
        private void OpenItemExec (object control, ExecutedRoutedEventArgs e)
        {
            SetBusyState();
            EntryViewModel entry = null;
            var lvi = e.OriginalSource as ListViewItem;
            if (lvi != null)
                entry = lvi.Content as EntryViewModel;
            if (null == entry)
                entry = CurrentDirectory.SelectedItem as EntryViewModel;
            if (null == entry)
                return;
            if ("audio" == entry.Type)
            {
                StartAudioPlayback (entry.Source);
                return;
            }
            else if ("video" == entry.Type)
            {
                StartVideoPlayback (entry.Source);
                return;
            }
            OpenDirectoryEntry (ViewModel, entry);
        }

        private void DescendExec (object control, ExecutedRoutedEventArgs e)
        {
            var entry = CurrentDirectory.SelectedItem as EntryViewModel;
            if (entry != null)
                OpenDirectoryEntry (ViewModel, entry);
        }

        private void AscendExec (object control, ExecutedRoutedEventArgs e)
        {
            var vm = ViewModel;
            var parent_dir = vm.FirstOrDefault (entry => entry.Name == "..");
            if (parent_dir != null)
                OpenDirectoryEntry (vm, parent_dir);
        }

        private void OpenDirectoryEntry (DirectoryViewModel vm, EntryViewModel entry)
        {
            string old_dir = null == vm ? "" : vm.Path.Last();
            string new_dir = entry.Source.Name;
            if (".." == new_dir)
            {
                if (null != vm && !vm.IsArchive)
                    new_dir = Path.Combine (old_dir, entry.Name);
                if (vm.Path.Count > 1 && string.IsNullOrEmpty (old_dir))
                    old_dir = vm.Path[vm.Path.Count - 2];
            }
            Trace.WriteLine (new_dir, "OpenDirectoryEntry");
            int old_fs_count = VFS.Count;
            vm = TryCreateViewModel (new_dir);
            if (null == vm)
            {
                if (VFS.Count == old_fs_count)
                    return;
                vm = new DirectoryViewModel (VFS.FullPath, new Entry[0], VFS.IsVirtual);
                PushViewModel (vm);
            }
            else
            {
                PushViewModel (vm);
                if (VFS.Count > old_fs_count && null != VFS.CurrentArchive)
                    ShowCurrentArchiveStatus();
                else{
                    SetFileStatus("");}
            }
            if (".." == entry.Name)
                lv_SelectItem (Path.GetFileName (old_dir));
            else
                lv_SelectItem (0);
        }

        #region Preview Management

        private readonly BackgroundWorker m_preview_worker = new BackgroundWorker();
        private PreviewFile m_current_preview = new PreviewFile();
        private bool m_preview_pending = false;

        private UIElement m_active_viewer;
        public UIElement ActiveViewer
        {
            get { return m_active_viewer; }
            set
            {
                if (value == m_active_viewer)
                    return;
                m_active_viewer = value;
                m_active_viewer.Visibility = Visibility.Visible;
                bool exists = false;
                foreach (UIElement c in PreviewPane.Children)
                {
                    if (c != m_active_viewer)
                        c.Visibility = Visibility.Collapsed;
                    else
                        exists = true;
                }
                if (!exists)
                    PreviewPane.Children.Add (m_active_viewer);
            }
        }

        /// <summary>
        /// Show video preview and hide other viewers.
        /// </summary>
        public void ShowVideoPreview (VideoPreviewControl videoControl)
        {
            ImageCanvas.Visibility = Visibility.Collapsed;
            m_animated_image_viewer.Visibility = Visibility.Collapsed;
            videoControl.Visibility = Visibility.Visible;
            ActiveViewer = videoControl;
            UpdateMediaControlsVisibility (MediaType.Video);
        }

        /// <summary>
        /// Show image preview and hide other viewers.
        /// </summary>
        public void ShowImagePreview()
        {
            m_animated_image_viewer.Visibility = Visibility.Collapsed;
            if (m_video_preview_ctl != null)
                m_video_preview_ctl.Visibility = Visibility.Collapsed;
            ImageCanvas.Visibility = Visibility.Visible;
            ActiveViewer = ImageView;
        }

        /// <summary>
        /// Show animated image preview and hide other viewers.
        /// </summary>
        public void ShowAnimatedImagePreview (ImagePreviewControl animatedViewer)
        {
            ImageCanvas.Visibility = Visibility.Collapsed;
            if (m_video_preview_ctl != null)
                m_video_preview_ctl.Visibility = Visibility.Collapsed;
            animatedViewer.Visibility = Visibility.Visible;
            ActiveViewer = animatedViewer;
        }

        /// <summary>
        /// Show text preview and hide other viewers (except audio controls if playing).
        /// </summary>
        public void ShowTextPreview()
        {
            ImageCanvas.Visibility = Visibility.Collapsed;
            m_animated_image_viewer.Visibility = Visibility.Collapsed;
            if (m_video_preview_ctl != null)
                m_video_preview_ctl.Visibility = Visibility.Collapsed;
            TextView.Visibility = Visibility.Visible;
            ActiveViewer = TextView;
        }

        /// <summary>
        /// Initialize preview pane and handlers.
        /// </summary>
        private void InitPreviewPane ()
        {
            m_preview_worker.DoWork += (s, e) => LoadPreviewImage (e.Argument as PreviewFile);
            m_preview_worker.RunWorkerCompleted += (s, e) => {
                if (m_preview_pending)
                    RefreshPreviewPane();
            };

            m_animated_image_viewer = new ImagePreviewControl();
            m_animated_image_viewer.Stretch = ImageCanvas.Stretch;
            m_animated_image_viewer.StretchDirection = ImageCanvas.StretchDirection;

            m_video_preview_ctl = new VideoPreviewControl();
            m_video_preview_ctl.StatusChanged += (status) => {
                m_video_base_info = status;
                SetPreviewStatus (status);
            };
            m_video_preview_ctl.PositionChanged += (pos, dur) => UpdateVideoPosition (pos, dur);
            m_video_preview_ctl.MediaEnded += () => OnVideoEnded (null, null);
            m_video_preview_ctl.PlaybackStateChanged += (isPlaying) => UpdateVideoControls (isPlaying);

            PreviewPane.Children.Add (m_animated_image_viewer);
            PreviewPane.Children.Add (m_video_preview_ctl);

            ActiveViewer = ImageView;
            TextView.IsWordWrapEnabled = true;

            _imagePreviewHandler = new ImagePreviewHandler (this, ImageCanvas, m_animated_image_viewer);
            _audioPreviewHandler = new AudioPreviewHandler (this);
            _videoPreviewHandler = new VideoPreviewHandler (this, m_video_preview_ctl);
            _textPreviewHandler = new TextPreviewHandler (this);
        }

        private IEnumerable<Encoding> m_encoding_list = GetEncodingList();
        public IEnumerable<Encoding> TextEncodings { get { return m_encoding_list; } }

        internal static IEnumerable<Encoding> GetEncodingList (bool exclude_utf16 = false)
        {
            var list = new HashSet<Encoding>();
            try
            {
                list.Add (Encoding.Default);
                var oem = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
                list.Add (Encoding.GetEncoding (oem));
            }
            catch (Exception X)
            {
                if (X is ArgumentException || X is NotSupportedException)
                    list.Add (Encoding.GetEncoding (20127)); //default to US-ASCII
                else
                    throw;
            }
            list.Add (Encoding.GetEncoding (932));
            list.Add (Encoding.GetEncoding (936));
            list.Add (Encoding.UTF8);
            if (!exclude_utf16)
            {
                list.Add (Encoding.Unicode);
                list.Add (Encoding.BigEndianUnicode);
                //list.Add (Encoding.UTF32);
                //list.Add (new UTF32Encoding(true, true));
            }
            return list;
        }

        private bool _isManualEncodingChange = false;

        private void OnEncodingSelect (object sender, SelectionChangedEventArgs e)
        {
            var enc = this.EncodingChoice.SelectedItem as Encoding;
            if (null == enc || _textPreviewHandler == null || !_textPreviewHandler.IsActive)
                return;

            _isManualEncodingChange = true;

            if (m_current_preview != null)
            {
                var fileIdentifier = GetFileIdentifier (m_current_preview);
                RememberFileEncoding (fileIdentifier, enc);
            }

            RefreshPreviewPane();
            _isManualEncodingChange = false;
        }

        /// <summary>
        /// Display entry in preview panel.
        /// </summary>
        private void PreviewEntry (Entry entry)
        {
            if (m_current_preview.IsEqual (ViewModel.Path, entry) && entry.Type != "video")
                return;
            UpdatePreviewPane (entry);
        }

        void RefreshPreviewPane()
        {
            m_preview_pending = false;
            var current = CurrentDirectory.SelectedItem as EntryViewModel;
            if (null != current)
                UpdatePreviewPane (current.Source);
            else
                ResetPreviewPane();
        }

        /// <summary>
        /// Reset all preview panes to default state.
        /// </summary>
        void ResetPreviewPane()
        {
            ActiveViewer = ImageView;

            _imagePreviewHandler?.Reset();
            _videoPreviewHandler?.Reset();
            _textPreviewHandler?.Reset();
            // Note: Don't reset audio handler to keep audio playing

            m_video_base_info = "";
        }

        bool IsPreviewPossible (Entry entry)
        {
            return "image" == entry.Type || "script" == entry.Type || "text" == entry.Type ||
                   "config" == entry.Type || "video" == entry.Type ||
                   (string.IsNullOrEmpty (entry.Type) && entry.Size < 0x100000);
        }

        /// <summary>
        /// Update preview pane based on entry type.
        /// </summary>
        void UpdatePreviewPane (Entry entry)
        {
            var vm = ViewModel;
            var previousPreview = m_current_preview;
            m_current_preview = new PreviewFile { Path = vm.Path, Name = entry.Name, Entry = entry, TempFile = null };

            if (!_isManualEncodingChange)
            {
                if (!m_current_preview.IsEqual (previousPreview?.Path, previousPreview?.Entry))
                {
                    if (entry.Type == "script" || entry.Type == "text" || entry.Type == "config" ||
                        (string.IsNullOrEmpty (entry.Type) && entry.Size < 0x100000))
                    {
                        var fileIdentifier = GetFileIdentifier (m_current_preview);
                        var rememberedEncoding = GetRememberedEncoding (fileIdentifier);
                        if (rememberedEncoding != null)
                            EncodingChoice.SelectedItem = rememberedEncoding;
                        else
                            EncodingChoice.SelectedItem = null;
                    }
                }
            }

            if ("video" == entry.Type)
                StopAudioPlayback();  // since videos autoplay on preview
            else if ("audio" == entry.Type && _videoPreviewHandler.IsActive)
                StopVideoPlayback();  // stop video when audio is selected
            else if (!_audioPreviewHandler.IsActive){
                UpdateMediaControlsVisibility (MediaType.None);
                SetFileStatus("");
                SetPreviewStatus("");
            }

            //if (!(_audioPreviewHandler.IsActive && entry.Type != "audio"))
                //SetFileStatus("");

            if (!IsPreviewPossible (entry))
            {
                ResetPreviewPane();
                return;
            }

            if ("video" == entry.Type)
            {
                _videoPreviewHandler.LoadContent (m_current_preview);
            }
            else if ("audio" == entry.Type)
            {
                StartAudioPlayback (entry);
            }
            else if ("image" != entry.Type)
            {
                _textPreviewHandler.LoadContent (m_current_preview);
                // Keep audio controls visible if audio is playing
                if (_audioPreviewHandler != null && _audioPreviewHandler.IsActive)
                    UpdateMediaControlsVisibility (MediaType.Audio);
            }
            else if (!m_preview_worker.IsBusy)
            {
                m_preview_worker.RunWorkerAsync (m_current_preview);
            }
            else
            {
                m_preview_pending = true;
            }
        }

        /// <summary>
        /// Load preview image using background worker.
        /// </summary>
        void LoadPreviewImage (PreviewFile preview)
        {
            _imagePreviewHandler.LoadContent (preview);
        }

        #endregion

        #region Audio Playback Management

        /// <summary>
        /// Start audio playback for entry.
        /// </summary>
        private void StartAudioPlayback (Entry entry)
        {
            StopVideoPlayback();
            StopAudioPlayback();

            var preview = new PreviewFile { Entry = entry, Name = entry.Name, Path = ViewModel.Path, TempFile = null };
            _audioPreviewHandler.LoadContent (preview);
        }

        /// <summary>
        /// Stop audio playback.
        /// </summary>
        private void StopAudioPlayback ()
        {
            if (_audioPreviewHandler != null && _audioPreviewHandler.IsActive)
            {
                _audioPreviewHandler.Reset();
                if (_currentMediaType == MediaType.Audio)
                {
                    UpdateMediaControlsVisibility (MediaType.None);
                    SetPreviewStatus("");
                }
            }
        }

        /// <summary>
        /// Pause/resume audio playback.
        /// </summary>
        private void PauseAudioPlayback ()
        {
            _audioPreviewHandler?.Pause();
        }

        /// <summary>
        /// Update audio control button states.
        /// </summary>
        internal void UpdateAudioControls()
        {
            bool isPaused = _audioPreviewHandler?.IsPaused ?? false;
            if (_currentMediaType == MediaType.Video)
                isPaused = !(_videoPreviewHandler?.IsPlaying ?? false);

            _mediaControl.UpdateButtonStates (isPaused, _isAutoPlaying, _isAutoCycling);
        }

        /// <summary>
        /// Get auto play status string.
        /// </summary>
        internal string GetAutoPlayStatus()
        {
            if (_isAutoCycling)
            {
                if (_isAutoPlaying)
                    return Localization._T("Repeat (Dir)");
                else
                    return Localization._T("Repeat (File)");
            }
            else if (_isAutoPlaying)
            {
                return Localization._T("Auto (Dir)");
            }
            return Localization._T("Manual");
        }

        /// <summary>
        /// Handle playback stopped event.
        /// </summary>
        internal void OnPlaybackStopped (object sender, StoppedEventArgs e)
        {
            if (_isShuttingDown)
                return;

            try
            {
                if (_isAutoCycling || _isAutoPlaying)
                {
                    if (PlayNextAudio())
                        return;
                    else
                    {
                        if (_isAutoPlaying && !_isAutoCycling)
                            SetFileStatus (guiStrings.MsgReachedLastAudio);
                    }
                }
                StopAudioPlayback();
            }
            catch (Exception X)
            {
                Trace.WriteLine (X.Message, "[OnPlaybackStopped]");
            }
        }

        /// <summary>
        /// Play next audio file in directory.
        /// </summary>
        private bool PlayNextAudio ()
        {
            int currentIndex = CurrentDirectory.SelectedIndex;
            if (currentIndex < 0)
                return false;

            for (int i = currentIndex; i < CurrentDirectory.Items.Count; i++)
            {
                var next_i = i + 1;
                if (_isAutoCycling)
                {
                    if (!_isAutoPlaying)
                        next_i = i;
                    else if (next_i >= CurrentDirectory.Items.Count)
                    {
                        next_i = 0;
                        var firstEntry = CurrentDirectory.Items[next_i] as EntryViewModel;
                        while (firstEntry.Source is SubDirEntry && next_i < CurrentDirectory.Items.Count - 2)
                        {
                            next_i++;
                            firstEntry = CurrentDirectory.Items[next_i] as EntryViewModel;
                        }
                    }
                }
                else
                {
                    if (next_i >= CurrentDirectory.Items.Count)
                        return false;
                }

                var nextEntry = CurrentDirectory.Items[next_i] as EntryViewModel;
                if (nextEntry != null && nextEntry.Type == "audio")
                {
                    CurrentDirectory.SelectedIndex = next_i;
                    StartAudioPlayback (nextEntry.Source);
                    return true;
                }
            }
            return false;
        }

        #endregion

        #region Video Playback Management

        /// <summary>
        /// Start video playback for entry.
        /// </summary>
        private void StartVideoPlayback (Entry entry)
        {
            StopVideoPlayback();
            StopAudioPlayback();

            if (m_video_preview_ctl != null && m_video_preview_ctl.Visibility == Visibility.Visible)
            {
                m_video_preview_ctl.Stop();
                m_video_preview_ctl.Play();
            }
            else
            {
                PreviewEntry (entry);
            }
            UpdateMediaControlsVisibility (MediaType.Video);
        }

        /// <summary>
        /// Stop video playback.
        /// </summary>
        private void StopVideoPlayback ()
        {
            if (_videoPreviewHandler != null && _videoPreviewHandler.IsActive)
            {
                _videoPreviewHandler.Reset();
                if (_currentMediaType == MediaType.Video && !_audioPreviewHandler.IsActive)
                {
                    UpdateMediaControlsVisibility (MediaType.None);
                    SetPreviewStatus ("");
                }
            }
        }

        /// <summary>
        /// Stop animation playback.
        /// </summary>
        private void StopAnimationPlayback ()
        {
            if (m_animated_image_viewer != null)
                m_animated_image_viewer.StopAnimation();
        }

        private void UpdateVideoPosition (TimeSpan position, TimeSpan duration)
        {
            if (m_video_preview_ctl == null || !m_video_preview_ctl.IsVisible)
                return;
            var timeInfo = $"{FormatTimeSpan (position)} / {FormatTimeSpan (duration)}";
            if (!string.IsNullOrEmpty (m_video_preview_ctl.CurrentCodecInfo))
                SetPreviewStatus($"{m_video_preview_ctl.CurrentCodecInfo} | {timeInfo}");
            else
                SetPreviewStatus (Localization.Format("Video: {}", timeInfo));
        }

        private void UpdateVideoControls (bool isPlaying)
        {
            UpdateAudioControls();
        }

        private string FormatTimeSpan (TimeSpan timeSpan)
        {
            return timeSpan.Hours > 0
                ? $"{timeSpan.Hours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}"
                : $"{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
        }

        private void OnVideoEnded (object sender, RoutedEventArgs e)
        {
            StopVideoPlayback();
        }

        private void OnVideoFailed (object sender, ExceptionRoutedEventArgs e)
        {
            StopVideoPlayback();
            SetFileStatus (Localization.Format("Video playback failed: {}", e.ErrorException.Message));
        }

        #endregion

        #region Media Control Commands

        /// <summary>
        /// Handle volume slider changes.
        /// </summary>
        private void MediaVolumeSlider_ValueChanged (object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_currentMediaType == MediaType.Audio && _audioPreviewHandler != null)
                _audioPreviewHandler.SetVolume((float)e.NewValue);
            else if (_currentMediaType == MediaType.Video && _videoPreviewHandler != null)
                _videoPreviewHandler.SetVolume (e.NewValue);
        }

        /// <summary>
        /// Execute stop playback command.
        /// </summary>
        private void StopPlaybackExec (object sender, ExecutedRoutedEventArgs e)
        {
            if (_currentMediaType == MediaType.Audio)
                StopAudioPlayback();
            else if (_currentMediaType == MediaType.Video)
                StopVideoPlayback();
        }

        /// <summary>
        /// Execute pause playback command.
        /// </summary>
        private void PausePlaybackExec (object sender, ExecutedRoutedEventArgs e)
        {
            if (_currentMediaType == MediaType.Audio)
                PauseAudioPlayback();
            else if (_currentMediaType == MediaType.Video)
            {
                if (_videoPreviewHandler.IsPlaying)
                    _videoPreviewHandler.Pause();
                else
                    _videoPreviewHandler.Play();
            }
        }

        /// <summary>
        /// Toggle cycle playback mode.
        /// </summary>
        private void CyclePlaybackExec (object sender, ExecutedRoutedEventArgs e)
        {
            _isAutoCycling = !_isAutoCycling;
            UpdateAudioControls();
        }

        /// <summary>
        /// Toggle auto playback mode.
        /// </summary>
        private void AutoPlaybackExec (object sender, ExecutedRoutedEventArgs e)
        {
            _isAutoPlaying = !_isAutoPlaying;
            UpdateAudioControls();
        }

        #endregion

        #region Stream Management

        /// <summary>
        /// Register a stream for cleanup.
        /// </summary>
        internal void RegisterStream (IDisposable stream)
        {
            lock (_activeStreams)
            {
                _activeStreams.Add (stream);
            }
        }

        /// <summary>
        /// Remove a stream from cleanup list.
        /// </summary>
        internal void RemoveStream (IDisposable stream)
        {
            lock (_activeStreams)
            {
                _activeStreams.Remove (stream);
            }
        }

        /// <summary>
        /// Dispose all active streams.
        /// </summary>
        private void DisposeAllStreams ()
        {
            lock (_activeStreams)
            {
                foreach (var stream in _activeStreams)
                {
                    try
                    {
                        stream.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine ($"Error disposing stream: {ex.Message}");
                    }
                }
                _activeStreams.Clear();
            }
        }

        #endregion

        /// <summary>
        /// Launch specified file.
        /// </summary>
        private void SystemOpen (string file)
        {
            try
            {
                Process.Start (file);
            }
            catch (Exception X)
            {
                SetFileStatus (X.Message);
            }
        }

        /// <summary>
        /// Refresh current view.
        /// </summary>
        private void RefreshExec (object sender, ExecutedRoutedEventArgs e)
        {
            RefreshView();
        }

        public void RefreshView()
        {
            VFS.Flush();
            var pos = GetCurrentPosition();
            SetCurrentPosition (pos);
        }

        /// <summary>
        /// Open current file in Explorer.
        /// </summary>
        private void ExploreItemExec (object sender, ExecutedRoutedEventArgs e)
        {
            var entry = CurrentDirectory.SelectedItem as EntryViewModel;
            if (entry != null && !ViewModel.IsArchive)
            {
                try
                {
                    string name = Path.Combine (CurrentPath, entry.Name);
                    Process.Start ("explorer.exe", "/select," + name);
                }
                catch (Exception X)
                {
                    Trace.WriteLine (X.Message, "explorer.exe");
                }
            }
        }

        /// <summary>
        /// Delete item from both media library and disk drive.
        /// </summary>
        private void DeleteItemExec (object sender, ExecutedRoutedEventArgs e)
        {
            var items = CurrentDirectory.SelectedItems.Cast<EntryViewModel>().Where (f => !f.IsDirectory);
            if (!items.Any())
                return;

            this.IsEnabled = false;
            try
            {
                VFS.Flush();
                ResetPreviewPane();
                if (!items.Skip (1).Any()) // items.Count() == 1
                {
                    string item_name = Path.Combine (CurrentPath, items.First().Name);
                    Trace.WriteLine (item_name, "DeleteItemExec");
                    FileOperationHelper.DeleteToRecycleBin (item_name, true); // true = show dialogs
                    DeleteItem (lv_GetCurrentContainer());
                    SetFileStatus (string.Format (guiStrings.MsgDeletedItem, item_name));
                }
                else
                {
                    int count = 0;
                    StopWatchDirectoryChanges();
                    try
                    {
                        var file_list = items.Select (entry => Path.Combine (CurrentPath, entry.Name));
                        if (!GARbro.Shell.File.Delete (file_list, new WindowInteropHelper (this).Handle))
                            throw new ApplicationException("Delete operation failed.");
                        count = file_list.Count();
                    }
                    finally
                    {
                        ResumeWatchDirectoryChanges();
                    }
                    RefreshView();
                    SetFileStatus (Localization.Format("MsgDeletedItems", count));
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception X)
            {
                SetFileStatus (X.Message);
            }
            finally
            {
                this.IsEnabled = true;
            }
        }

        /// <summary>
        /// Delete item at the specified position within ListView, correctly adjusting current.
        /// position.
        /// </summary>
        private void DeleteItem (ListViewItem item)
        {
            int i = CurrentDirectory.SelectedIndex;
            int next = -1;
            if (i + 1 < CurrentDirectory.Items.Count)
                next = i + 1;
            else if (i > 0)
                next = i - 1;

            if (next != -1)
                CurrentDirectory.SelectedIndex = next;

            var entry = item.Content as EntryViewModel;
            if (entry != null)
            {
                ViewModel.Remove (entry);
            }
        }

        /// <summary>
        /// Rename selected item.
        /// </summary>
        private void RenameItemExec (object sender, ExecutedRoutedEventArgs e)
        {
            RenameElement (lv_GetCurrentContainer());
        }

        /// <summary>
        /// Rename item contained within specified framework control.
        /// </summary>
        void RenameElement (ListViewItem item)
        {
            if (item == null)
                return;
            /*
            TextBlock block = FindByName (item, "item_Text") as TextBlock;
            TextBox box = FindSibling (block, "item_Input") as TextBox;

            if (block == null || box == null)
                return;

            IsRenameActive = true;

            block.Visibility = Visibility.Collapsed;
            box.Text = block.Text;
            box.Visibility = Visibility.Visible;
            box.Select (0, box.Text.Length);
            box.Focus();
            */
        }

        /// <summary>
        /// Select files matching mask.
        /// </summary>
        void AddSelectionExec (object sender, ExecutedRoutedEventArgs e)
        {
            try
            {
                var ext_list = new SortedSet<string>();
                foreach (var entry in ViewModel)
                {
                    var ext = Path.GetExtension (entry.Name).ToLowerInvariant();
                    if (!string.IsNullOrEmpty (ext))
                        ext_list.Add (ext);
                }
                var selection = new EnterMaskDialog (ext_list.Select (ext => "*" + ext));
                selection.Owner = this;
                var result = selection.ShowDialog();
                if (!result.Value)
                    return;
                if ("*.*" == selection.Mask.Text)
                {
                    CurrentDirectory.SelectAll();
                    return;
                }
                SetBusyState();
                var glob = new FileNameGlob (selection.Mask.Text);
                var matching = ViewModel.Where (entry => glob.IsMatch (entry.Name));
                if (!matching.Any())
                {
                    SetFileStatus (string.Format (guiStrings.MsgNoMatching, selection.Mask.Text));
                    return;
                }
                var selected = CurrentDirectory.SelectedItems.Cast<EntryViewModel>();
                matching = matching.Except (selected).ToList();
                int count = matching.Count();
                CurrentDirectory.SetSelectedItems (selected.Concat (matching));
                if (count != 0)
                    SetFileStatus (count.Pluralize("MsgSelectedFiles"));
            }
            catch (Exception X)
            {
                SetFileStatus (X.Message);
            }
        }

        void SelectAllExec (object sender, ExecutedRoutedEventArgs e)
        {
            CurrentDirectory.SelectAll();
        }

        void CopyNamesExec (object sender, ExecutedRoutedEventArgs e)
        {
            var names = CurrentDirectory.SelectedItems.Cast<EntryViewModel>().Select (f => f.Name);
            if (names.Any())
            {
                try
                {
                    Clipboard.SetText (string.Join("\r\n", names));
                }
                catch (Exception X)
                {
                    Trace.WriteLine (X.Message, "Clipboard error");
                }
            }
        }

        void NextItemExec (object sender, ExecutedRoutedEventArgs e)
        {
            if (LookupActive)
                return;

            var index = CurrentDirectory.SelectedIndex + 1;
            if (index < CurrentDirectory.Items.Count)
            {
                CurrentDirectory.SelectedIndex = index;
                CurrentDirectory.ScrollIntoView (CurrentDirectory.SelectedItem);
            }
        }

        /// <summary>
        /// Handle "Exit" command.
        /// </summary>
        void ExitExec (object sender, ExecutedRoutedEventArgs e)
        {
            StopAudioPlayback();
            StopVideoPlayback();
            Application.Current.Shutdown();
        }

        private void AboutExec (object sender, ExecutedRoutedEventArgs e)
        {
            var about = new AboutBox();
            about.Owner = this;
            about.ShowDialog();
        }

        private void PreferencesExec (object sender, ExecutedRoutedEventArgs e)
        {
            var settings = new SettingsWindow();
            settings.Owner = this;
            settings.ShowDialog();
        }

        private void TroubleShootingExec (object sender, ExecutedRoutedEventArgs e)
        {
            var dialog = new TroubleShootingDialog();
            dialog.Owner = this;
            dialog.ShowDialog();
        }

        private void ScaleImageExec (object sender, ExecutedRoutedEventArgs e)
        {
            DownScaleImage.Value = !DownScaleImage.Get<bool>();
        }

        /// <summary>
        /// Apply down scale setting to image viewer.
        /// </summary>
        internal void ApplyDownScaleSetting()
        {
            bool image_need_scale = DownScaleImage.Get<bool>();

            if (image_need_scale)
            {
                if (ImageCanvas.Source != null)
                {
                    var image = ImageCanvas.Source;
                    image_need_scale = image.Width > ImageView.ActualWidth || image.Height > ImageView.ActualHeight;
                }
                else if (m_animated_image_viewer.Source != null)
                {
                    var image = m_animated_image_viewer.Source;
                    image_need_scale = image.Width > ImageView.ActualWidth || image.Height > ImageView.ActualHeight;
                }
            }

            SetImageScaleMode (image_need_scale);
        }

        /// <summary>
        /// Apply scaling to animated image viewer.
        /// </summary>
        internal void ApplyScalingToAnimatedViewer()
        {
            bool image_need_scale = DownScaleImage.Get<bool>();
            if (image_need_scale && m_animated_image_viewer.Source != null)
            {
                var image = m_animated_image_viewer.Source;
                image_need_scale = image.Width > ImageView.ActualWidth || image.Height > ImageView.ActualHeight;
            }

            if (image_need_scale)
            {
                m_animated_image_viewer.Stretch = Stretch.Uniform;
                RenderOptions.SetBitmapScalingMode (m_animated_image_viewer, BitmapScalingMode.HighQuality);
            }
            else
            {
                m_animated_image_viewer.Stretch = Stretch.None;
                RenderOptions.SetBitmapScalingMode (m_animated_image_viewer, BitmapScalingMode.NearestNeighbor);
            }
        }

        private void SetImageScaleMode (bool scale)
        {
            if (scale)
            {
                ImageCanvas.Stretch = Stretch.Uniform;
                RenderOptions.SetBitmapScalingMode (ImageCanvas, BitmapScalingMode.HighQuality);
                ImageView.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                ImageView.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;

                m_animated_image_viewer.Stretch = Stretch.Uniform;
                RenderOptions.SetBitmapScalingMode (m_animated_image_viewer, BitmapScalingMode.HighQuality);
            }
            else
            {
                ImageCanvas.Stretch = Stretch.None;
                RenderOptions.SetBitmapScalingMode (ImageCanvas, BitmapScalingMode.NearestNeighbor);
                ImageView.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                ImageView.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;

                m_animated_image_viewer.Stretch = Stretch.None;
                RenderOptions.SetBitmapScalingMode (m_animated_image_viewer, BitmapScalingMode.NearestNeighbor);
            }
        }

        /// <summary>
        /// Fit window size to image.
        /// </summary>
        private void FitWindowExec (object sender, ExecutedRoutedEventArgs e)
        {
            DownScaleImage.Value = !DownScaleImage.Get<bool>();
        }

        private void PreviewSizeChanged (object sender, SizeChangedEventArgs e)
        {
            ImageSource image = null;
            if (ImageCanvas.Visibility == Visibility.Visible)
                image = ImageCanvas.Source;
            else if (m_animated_image_viewer.Visibility == Visibility.Visible)
                image = m_animated_image_viewer.Source;

            if (null == image || !DownScaleImage.Get<bool>())
                return;

            SetImageScaleMode (image.Width > e.NewSize.Width || image.Height > e.NewSize.Height);
        }

        private void OnEntrySelectionChanged (object sender, SelectionChangedEventArgs e)
        {
            if (m_animated_image_viewer != null)
                m_animated_image_viewer.StopAnimation();

            if (m_video_preview_ctl != null)
                m_video_preview_ctl.Stop();
        }

        private void CanExecuteScaleImage (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = ImageCanvas.Source != null;
        }

        private void CanExecuteAlways (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }

        private void CanExecuteControlCommand (object sender, CanExecuteRoutedEventArgs e)
        {
            Control target = e.Source as Control;
            e.CanExecute = target != null;
        }

        private void CanExecuteOnSelected (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = CurrentDirectory.SelectedIndex != -1;
        }

        private void CanExecutePlaybackControl (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = _currentMediaType != MediaType.None;
        }

        private void CanExecuteConvertMedia (object sender, CanExecuteRoutedEventArgs e)
        {
            if (CurrentDirectory.SelectedItems.Count >= 1)
            {
                e.CanExecute = !ViewModel.IsArchive;
            }
        }

        private void CanExecuteOnImage (object sender, CanExecuteRoutedEventArgs e)
        {
            var entry = CurrentDirectory.SelectedItem as EntryViewModel;
            e.CanExecute = !ViewModel.IsArchive && entry != null && entry.Type == "image";
        }

        private void CanExecuteInArchive (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = ViewModel.IsArchive && CurrentDirectory.SelectedIndex != -1;
        }

        private void CanExecuteCreateArchive (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = !ViewModel.IsArchive && CurrentDirectory.SelectedItems.Count > 0;
        }

        private void CanExecuteInDirectory (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = !ViewModel.IsArchive;
        }

        private void CanExecuteExtract (object sender, CanExecuteRoutedEventArgs e)
        {
            if (ViewModel.IsArchive)
            {
                e.CanExecute = true;
                return;
            }
            else if (CurrentDirectory.SelectedIndex != -1)
            {
                var entry = CurrentDirectory.SelectedItem as EntryViewModel;
                if (entry != null && !entry.IsDirectory)
                {
                    e.CanExecute = true;
                    return;
                }
            }
            e.CanExecute = false;
        }

        private void CanExecuteOnPhysicalFile (object sender, CanExecuteRoutedEventArgs e)
        {
            if (!ViewModel.IsArchive && CurrentDirectory.SelectedIndex != -1)
            {
                var entry = CurrentDirectory.SelectedItem as EntryViewModel;
                if (entry != null && !entry.IsDirectory)
                {
                    e.CanExecute = true;
                    return;
                }
            }
            e.CanExecute = false;
        }

        private void OnParametersRequest (object sender, ParametersRequestEventArgs e)
        {
            var format = sender as IResource;
            if (null != format)
            {
                var control = format.GetAccessWidget() as UIElement;
                if (null != control)
                {
                    bool busy_state = m_busy_state;
                    var param_dialog = new ArcParametersDialog (control, e.Notice);
                    param_dialog.Owner = this;
                    e.InputResult = param_dialog.ShowDialog() ?? false;
                    if (e.InputResult)
                        e.Options = format.GetOptions (control);
                    if (busy_state)
                        SetBusyState();
                }
            }
        }

        private void CanExecuteFitWindow (object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = ImageCanvas.Source != null;
        }

        private void HideStatusBarExec (object sender, ExecutedRoutedEventArgs e)
        {
            ToggleVisibility (AppStatusBar);
        }

        private void HideMenuBarExec (object sender, ExecutedRoutedEventArgs e)
        {
            ToggleVisibility (MainMenuBar);
        }

        private void HideToolBarExec (object sender, ExecutedRoutedEventArgs e)
        {
            ToggleVisibility (MainToolBar);
        }

        static void ToggleVisibility (UIElement item)
        {
            item.Visibility = (Visibility.Visible == item.Visibility) ? 
                Visibility.Collapsed : 
                Visibility.Visible;
        }

        private void OnDropEvent (object sender, DragEventArgs e)
        {
            try
            {
                if (!e.Data.GetDataPresent (DataFormats.FileDrop))
                    return;
                var files = (string[])e.Data.GetData (DataFormats.FileDrop);
                if (!files.Any())
                    return;
                var filename = files.First();
                try
                {
                    OpenFileOrDir (filename);
                }
                catch (Exception X)
                {
                    VFS.FullPath = new string[] { Path.GetDirectoryName (filename) };
                    var vm = new DirectoryViewModel (VFS.FullPath, VFS.GetFiles(), VFS.IsVirtual);
                    PushViewModel (vm);
                    filename = Path.GetFileName (filename);
                    lv_SelectItem (filename);
                    SetFileStatus (string.Format("{0}: {1}", filename, X.Message));
                }
            }
            catch (Exception X)
            {
                Trace.WriteLine (X.Message, "Drop event failed");
            }
        }

        private class FileEncodingEntry
        {
            public string FileIdentifier { get; set; }
            public int CodePage { get; set; }
        }

        private readonly Dictionary<string, int> _fileEncodingCache = new Dictionary<string, int>();
        private readonly LinkedList<FileEncodingEntry> _fileEncodingHistory = new LinkedList<FileEncodingEntry>();
        private const int MAX_ENCODING_HISTORY = 20;

        private void LoadEncodingHistory ()
        {
            try
            {
                if (Settings.Default.fileEncodingHistory != null)
                {
                    foreach (var entry in Settings.Default.fileEncodingHistory)
                    {
                        var parts = entry.Split('|');
                        if (parts.Length >= 2 && int.TryParse(parts[1], out int codePage))
                        {
                            var historyEntry = new FileEncodingEntry
                            {
                                FileIdentifier = parts[0],
                                CodePage = codePage
                            };
                            _fileEncodingHistory.AddLast(historyEntry);
                            _fileEncodingCache[parts[0]] = codePage;
                        }
                    }
                }
            }
            catch { }
        }

        private void SaveEncodingHistory ()
        {
            try
            {
                var settings = Settings.Default;
                if (settings.fileEncodingHistory == null)
                    settings.fileEncodingHistory = new StringCollection();
                else
                    settings.fileEncodingHistory.Clear ();

                foreach (var entry in _fileEncodingHistory)
                {
                    settings.fileEncodingHistory.Add ($"{entry.FileIdentifier}|{entry.CodePage}");
                }

                settings.Save();
            }
            catch { }
        }

        internal string GetFileIdentifier (PreviewFile preview)
        {
            if (preview?.Path != null && preview.Path.Any())
            {
                return string.Join("/", preview.Path.Concat(new[] { preview.Name }));
            }
            return preview?.Name ?? "";
        }

        internal void RememberFileEncoding (string fileIdentifier, Encoding encoding)
        {
            if (string.IsNullOrEmpty (fileIdentifier) || encoding == null)
                return;

            _fileEncodingCache[fileIdentifier] = encoding.CodePage;
            var existingEntry = _fileEncodingHistory.FirstOrDefault (e => e.FileIdentifier == fileIdentifier);
            if (existingEntry != null)
                _fileEncodingHistory.Remove(existingEntry);

            _fileEncodingHistory.AddFirst (new FileEncodingEntry
            {
                FileIdentifier = fileIdentifier,
                CodePage = encoding.CodePage
            });

            while (_fileEncodingHistory.Count > MAX_ENCODING_HISTORY)
                _fileEncodingHistory.RemoveLast();

            SaveEncodingHistory();
        }

        private Encoding GetRememberedEncoding (string fileIdentifier)
        {
            if (string.IsNullOrEmpty (fileIdentifier))
                return null;

            if (_fileEncodingCache.TryGetValue (fileIdentifier, out int codePage))
            {
                try
                {
                    return Encoding.GetEncoding (codePage);
                }
                catch
                {
                    _fileEncodingCache.Remove (fileIdentifier);
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Represents a file being previewed.
    /// </summary>
    public class PreviewFile : IDisposable
    {
        public IEnumerable<string> Path { get; set; }
        public              string Name { get; set; }
        public              Entry Entry { get; set; }
        public          string TempFile { get; set; }

        public bool IsEqual (IEnumerable<string> path, Entry entry)
        {
            return Path != null && entry != null && path != null && 
                    path.SequenceEqual (Path) && Entry == entry;
        }

        public bool IsRealFile
        {
            get
            {
                return string.IsNullOrEmpty (TempFile);
            }
        }

        public string GetDisplayName()
        {
            return Name ?? System.IO.Path.GetFileName (TempFile) ?? Localization._T("Unknown");
        }

        public void Dispose()
        {
            if (string.IsNullOrEmpty (TempFile)) return;
            if (File.Exists (TempFile))
            {
                try
                {
                    File.Delete (TempFile);
                }
                catch { }
            }
        }
    }

    public class SortModeToBooleanConverter : IValueConverter
    {
        public object Convert (object value, Type targetType, object parameter, CultureInfo culture)
        {
            string actual_mode = value as string;
            string check_mode = parameter as string;
            if (string.IsNullOrEmpty (check_mode))
                return string.IsNullOrEmpty (actual_mode);
            return check_mode.Equals (actual_mode);
        }

        public object ConvertBack (object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BooleanToCollapsedVisibilityConverter : IValueConverter
    {
        #region IValueConverter Members

        public object Convert (object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            //reverse conversion (false=>Visible, true=>collapsed) on any given parameter
            bool input = (null == parameter) ? (bool)value : !((bool)value);
            return (input) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack (object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        #endregion
    }

    public static class Commands
    {
        #region Commands Members

        public static readonly RoutedCommand OpenItem = new RoutedCommand();
        public static readonly RoutedCommand OpenFile = new RoutedCommand();
        public static readonly RoutedCommand OpenRecent = new RoutedCommand();
        public static readonly RoutedCommand ExtractItem = new RoutedCommand();
        public static readonly RoutedCommand CreateArchive = new RoutedCommand();
        public static readonly RoutedCommand SortBy = new RoutedCommand();
        public static readonly RoutedCommand Exit = new RoutedCommand();
        public static readonly RoutedCommand About = new RoutedCommand();
        public static readonly RoutedCommand CheckUpdates = new RoutedCommand();
        public static readonly RoutedCommand GoBack = new RoutedCommand();
        public static readonly RoutedCommand GoForward = new RoutedCommand();
        public static readonly RoutedCommand DeleteItem = new RoutedCommand();
        public static readonly RoutedCommand RenameItem = new RoutedCommand();
        public static readonly RoutedCommand ExploreItem = new RoutedCommand();
        public static readonly RoutedCommand ConvertMedia = new RoutedCommand();
        public static readonly RoutedCommand Refresh = new RoutedCommand();
        public static readonly RoutedCommand Browse = new RoutedCommand();
        public static readonly RoutedCommand FitWindow = new RoutedCommand();
        public static readonly RoutedCommand HideStatusBar = new RoutedCommand();
        public static readonly RoutedCommand HideMenuBar = new RoutedCommand();
        public static readonly RoutedCommand HideToolBar = new RoutedCommand();
        public static readonly RoutedCommand AddSelection = new RoutedCommand();
        public static readonly RoutedCommand SelectAll = new RoutedCommand();
        public static readonly RoutedCommand SetFileType = new RoutedCommand();
        public static readonly RoutedCommand NextItem = new RoutedCommand();
        public static readonly RoutedCommand CopyNames = new RoutedCommand();
        public static readonly RoutedCommand StopPlayback = new RoutedCommand();
        public static readonly RoutedCommand PausePlayback = new RoutedCommand();
        public static readonly RoutedCommand CyclePlayback = new RoutedCommand();
        public static readonly RoutedCommand AutoPlayback = new RoutedCommand();
        public static readonly RoutedCommand Preferences = new RoutedCommand();
        public static readonly RoutedCommand TroubleShooting = new RoutedCommand();
        public static readonly RoutedCommand Descend = new RoutedCommand();
        public static readonly RoutedCommand Ascend = new RoutedCommand();
        public static readonly RoutedCommand ScaleImage = new RoutedCommand();

        #endregion
    }

    public static class FileOperationHelper
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string pTo;
            public ushort fFlags;
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        private const uint FO_DELETE = 0x0003;
        private const ushort FOF_ALLOWUNDO = 0x0040;       // Send to Recycle Bin
        private const ushort FOF_NOCONFIRMATION = 0x0010;  // No confirmation dialog

        // NOTE: this project is Windows-only so this works better than VisualBasic .NET dependency
        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern int SHFileOperation (ref SHFILEOPSTRUCT FileOp);

        public static void DeleteToRecycleBin (string path, bool showDialog = true)
        {
            var fileOp = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = path + '\0' + '\0', // Double null-terminated
                fFlags = FOF_ALLOWUNDO
            };

            if (!showDialog)
            {
                fileOp.fFlags |= FOF_NOCONFIRMATION; // Remove FOF_NOCONFIRMATION if you want dialogs
            }
            SHFileOperation (ref fileOp);
        }
    }
}
