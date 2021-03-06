﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Deployment.Application;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Ribbon;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Microsoft.Win32;

using ImagingSIMS.Direct3DRendering;

using ImagingSIMS.Common;
using ImagingSIMS.Common.Dialogs;
using ImagingSIMS.Common.Math;
using ImagingSIMS.Common.Registry;
using ImagingSIMS.Controls;
using ImagingSIMS.Data;
using ImagingSIMS.Data.Imaging;
using ImagingSIMS.Data.Spectra;
using ImagingSIMS.Data.Rendering;
using ImagingSIMS.Controls.Tabs;
using ImagingSIMS.Controls.BaseControls;
using ImagingSIMS.Controls.BaseControls.SpectrumView;
using ImagingSIMS.Direct3DRendering.DrawingObjects;
using ImagingSIMS.Direct3DRendering.Controls;
using ImagingSIMS.Data.Colors;
using ImagingSIMS.Data.Converters;

using Accord.Math;
using Accord.Math.Decompositions;
using System.Threading.Tasks;

using Matrix = Accord.Math.Matrix;
using Accord.Statistics.Analysis;
using Accord.Statistics.Kernels;
using System.Globalization;
using System.Windows.Interop;

namespace ImagingSIMS.MainApplication
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : RibbonWindow, IAvailableTables, 
        IAvailableImageSeries, IAvailableVolumes, IAvailableSpectra
    {
        #region Properties
        public static readonly DependencyProperty WorkspaceProperty = DependencyProperty.Register("Workspace",
            typeof(Workspace), typeof(MainWindow));
        public static readonly DependencyProperty IsDebugProperty = DependencyProperty.Register("IsDebug",
            typeof(bool), typeof(MainWindow));
        public static readonly DependencyProperty ShowCodeProperty = DependencyProperty.Register("ShowCode",
            typeof(bool), typeof(MainWindow));

        //BackgroundWorker bw;
        ProgressWindow pw;
        TraceListenerWindow tlw;
        Splash _splashScreen;

        public Workspace Workspace
        {
            get { return (Workspace)GetValue(WorkspaceProperty); }
            set { SetValue(WorkspaceProperty, value); }
        }
        public bool IsDebug
        {
            get { return (bool)GetValue(IsDebugProperty); }
            set { SetValue(IsDebugProperty, value); }
        }
        public bool ShowCode
        {
            get { return (bool)GetValue(ShowCodeProperty); }
            set { SetValue(ShowCodeProperty, value); }
        }

        #endregion

        #region Load
        public MainWindow()
        {
            _splashScreen = new Splash();
            _splashScreen.Show();
#if DEBUG
            IsDebug = true;
#endif          
            Workspace = new Workspace();


            InitializeComponent();

        }
        private void RibbonWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (Workspace.Registry.StartWithTrace)
            {
                tlw = new TraceListenerWindow();
                Trace.Listeners.Add(tlw.TraceListener);

                tlw.Show();

                Trace.WriteLine("Trace listener added.");
            }

            InitializeCommands();

            Trace.WriteLine("Adding RoutedEventHandlers.");

            AddHandler(ClosableTabItem.CloseTabEvent, new RoutedEventHandler(CloseTab));
            AddHandler(ClosableTabItem.StatusUpdatedEvent, new StatusUpdatedRoutedEventHandler(StatusUpdated));
            AddHandler(ClosableTabItem.CloseMultipleTabsEvent, new CloseMultipleTabsEventHandler(CloseMultipleTabs));
            AddHandler(ComponentTab.ComponentCreatedEvent, new RoutedEventHandler(ComponentCreated));
            AddHandler(ComponentTab.ComponentUpdatedEvent, new RoutedEventHandler(ComponentUpdated));
            AddHandler(SpecChart.RangeUpdatedEvent, new RangeUpdatedRoutedEventHandler(SpecRangeUpdated));
            AddHandler(SpecChart.SelectionRangeUpdatedEvent, new RangeUpdatedRoutedEventHandler(SpecSelectionRangeUpdated));
            AddHandler(VolumeTab.VolumeCreatedEvent, new RoutedEventHandler(VolumeCreated));

            if (DesignerProperties.GetIsInDesignMode(this))
            {
                Workspace = Workspace.SampleWorkspace();
            }

            if (Workspace.Registry.ShowStartup)
            {
                StartupTab st = new StartupTab();
                st.RecentFileClicked += startupTab_RecentFileClicked;
                st.RecentFileRemoveClicked += startupTab_RecentFileRemoveClicked;
                tabMain.Items.Add(ClosableTabItem.Create(st, TabType.Startup));
                tabMain.SelectedIndex = 0;
            }

            Trace.WriteLine("Creating available color scales.");
            ColorScaleMenuItems = new List<MenuItem>();
            foreach (ColorScaleTypes type in EnumEx.EnumToList<ColorScaleTypes>())
            {
                MenuItem mi = new MenuItem()
                {
                    Header = EnumEx.Get(type)
                };
                mi.Click += CMDataPreviewScale;
                ColorScaleMenuItems.Add(mi);
            }

            Trace.WriteLine("Checking ClickOnce version information.");
            bool isClickOnce = ApplicationDeployment.IsNetworkDeployed;
            Trace.WriteLine("ClickOnce deployment: " + isClickOnce);

            // Debug won't have ClickOnce deployment information so pass in
            // a test version
            // Include option to test this or skip during debug
            bool testVersionCheck = true;
            if (IsDebug && testVersionCheck)
            {
                Version testVersion = new Version(3, 6, 2, 5);
                ChangeWindow.CheckAndShow(testVersion, "ChangeLog.json");
            }

            if (isClickOnce)
            {
                Version currentVersion = ApplicationDeployment.CurrentDeployment.CurrentVersion;
                ChangeWindow.CheckAndShow(currentVersion, "ChangeLog.json");
            }

            if (isClickOnce)
            {
                Trace.WriteLine("Checking ClickOnce activation arguments.");

                if (AppDomain.CurrentDomain.SetupInformation.ActivationArguments != null &&
                    AppDomain.CurrentDomain.SetupInformation.ActivationArguments.ActivationData != null)
                {
                    string[] args = AppDomain.CurrentDomain.SetupInformation.ActivationArguments.ActivationData;

                    if (args.Length > 0)
                    {
                        string original = args[0];
                        Uri uri = new Uri(original);

                        string path = uri.ToString();
                        path = path.Remove(0, 8);

                        if (path.EndsWith(".wks"))
                        {
                            Trace.WriteLine("Workspace argument found.");

                            Workspace = new Workspace(path);

                            UpdateRecentFiles(path);
                        }
                        else if (path.EndsWith(".isd"))
                        {
                            Trace.WriteLine("Sample data argument found.");

                            SampleData sd = new SampleData(path);
                            foreach (Data2D d in sd.SphereData.Layers)
                            {
                                Workspace.Data.Add(d);
                            }
                        }
                        else if (path.EndsWith(".vol"))
                        {
                            Trace.WriteLine("Volume argument found.");

                            Workspace.Volumes.Add(new Volume(path));
                        }
                    }
                }
            }

            Trace.WriteLine("Checking for autosaved workspace.");
            if (Workspace.Registry.HasCrashed)
            {
                Trace.WriteLine("Previous instance crashed. Attempting to load autosave file.");

                if (DialogBox.Show(string.Format("An autosave file was found from {0}.", Workspace.Registry.CrashDateTime),
                           "Click OK to load the workspace or cancel to continue. If you click cancel, the autosave will be deleted permanently.",
                           "Autosave", DialogIcon.Help, true) == false)
                {
                    Trace.WriteLine("Autosave aborted by user.");

                    File.Delete(Workspace.Registry.CrashFilePath);

                    Workspace.Registry.ClearCrashSettings();
                }
                else
                {
                    try
                    {
                        Workspace.Load(Workspace.Registry.CrashFilePath);

                        DialogBox.Show("The autosave was able to load the previous workspace.",
                            "You should manually save a copy of this workspace using the File menu.", "Recovery", DialogIcon.Ok);

                        File.Delete(Workspace.Registry.CrashFilePath);

                        Workspace.Registry.ClearCrashSettings();

                        Trace.WriteLine("Autosave loaded.");
                    }
                    catch (Exception)
                    {
                        DialogBox.Show("There was a problem loading the autosave file.",
                            "The program will now load a blank workspace. The autosave file will not be deleted and can be found at " +
                            Workspace.Registry.CrashFilePath, "Autosave", DialogIcon.Warning);
                        Workspace = new Workspace();
                    }
                }
            }

            Trace.WriteLine(string.Format("Is Debug: {0}", IsDebug));

            AvailableHost.AvailableTablesSource = this;
            AvailableHost.AvailableImageSeriesSource = this;
            AvailableHost.AvailableVolumesSource = this;
            AvailableHost.AvailableSpectraSource = this;

            Trace.WriteLine("Window load complete.");

            _splashScreen?.Close();
        }
        #endregion

        #region Close
        private void CloseTab(object sender, RoutedEventArgs e)
        {
            ClosableTabItem cti = e.Source as ClosableTabItem;
            doCloseTab(cti);
        }

        private void CloseMultipleTabs(object sender, CloseMultipleTabsRoutedEventArgs e)
        {
            ClosableTabItem source = e.Source as ClosableTabItem;
            if (source == null) return;

            TabControl parent = source.Parent as TabControl;
            if (parent == null) return;

            var itemsToRemove = new List<ClosableTabItem>();

            foreach (var item in parent.Items)
            {
                ClosableTabItem cti = item as ClosableTabItem;
                if (cti == null) continue;

                if (e.CloseAllButThis && cti == source)
                    continue;

                itemsToRemove.Add(cti);
            }

            foreach (var item in itemsToRemove)
            {
                doCloseTab(item);
            }
        }

        private void doCloseTab(ClosableTabItem cti)
        {
            if (cti == null) return;

            object content = cti.Content;
            if (content is SpectrumTab)
            {
                ((SpectrumTab)content).ClearResources();
            }
            else if (content is SettingsTab)
            {
                ((SettingsTab)content).OnCancel();
            }
            else if(content is FusionTab)
            {
                ((FusionTab)content).Dispose();
            }

            cti.CloseTab -= CloseTab;
            TabControl tc = cti.Parent as TabControl;
            if (tc != null)
            {
                tc.Items.Remove(cti);
            }
            cti.Dispose();
        }
        #endregion

        #region Application Menu
        private void ribbonButtonOptions_Click(object sender, RoutedEventArgs e)
        {
            ClosableTabItem cti = ClosableTabItem.Create(new SettingsTab(Workspace.Registry), TabType.Settings);
            tabMain.Items.Add(cti);
            tabMain.SelectedItem = cti;
        }
        private void ribbonButtonAbout_Click(object sender, RoutedEventArgs e)
        {
            About a = new About();
            a.Show();
        }
        private void ribbonButtonCleanup_Click(object sender, RoutedEventArgs e)
        {
            long allocatedBefore = GC.GetTotalMemory(false);

            GC.Collect();

            long allocatedAfter = GC.GetTotalMemory(true);

            DialogBox db = new DialogBox("A garbage collection has been requested. Below is the result of the operation.",
                string.Format("Memory usage:\nBefore collection: {0} bytes\nAfter Collection: {1} bytes\nMemory freed: {2} bytes ({3}%)",
                allocatedBefore, allocatedAfter, allocatedBefore - allocatedAfter, Percentage.GetPercent(allocatedBefore - allocatedAfter, allocatedBefore).ToString("0.00")),
                "Cleanup", DialogIcon.Information);
            db.ShowDialog();
        }
        private void ribbonButtonTraceWindow_Click(object sender, RoutedEventArgs e)
        {
            tlw = new TraceListenerWindow();
            Trace.Listeners.Add(tlw.TraceListener);

            tlw.Show();
        }
        private void ribbonButtonCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            UpdateCheckInfo info = null;

            if (ApplicationDeployment.IsNetworkDeployed)
            {
                ApplicationDeployment ad = ApplicationDeployment.CurrentDeployment;

                try
                {
                    info = ad.CheckForDetailedUpdate();
                }
                catch (DeploymentDownloadException dde)
                {
                    DialogBox db = new DialogBox("The new version of the application cannot be downloaded at this time. Please check your network connection, or try again later.",
                        dde.Message, "Update", DialogIcon.Error);
                    db.ShowDialog();
                    return;
                }
                catch (InvalidDeploymentException ide)
                {
                    DialogBox db = new DialogBox("Cannot check for a new version of the application. The ClickOnce deployment is corrupt. Please redeploy the application and try again.",
                        ide.Message, "Update", DialogIcon.Error);
                    db.ShowDialog();
                    return;
                }
                catch (InvalidOperationException ioe)
                {
                    DialogBox db = new DialogBox("This application cannot be updated. It is likely not a ClickOnce application.",
                        ioe.Message, "Update", DialogIcon.Error);
                    db.ShowDialog();
                    return;
                }

                if (info.UpdateAvailable)
                {
                    Boolean doUpdate = true;

                    if (!info.IsUpdateRequired)
                    {
                        DialogBox db = new DialogBox("An update is available!", "Would you like to update the application now?",
                            "Update Available", DialogIcon.Help, true);
                        if (db.ShowDialog() != true) doUpdate = false;
                    }
                    else
                    {
                        // Display a message that the app MUST reboot. Display the minimum required version.
                        DialogBox db = new DialogBox("This application has detected a mandatory update from your current " +
                            "version to version " + info.MinimumRequiredVersion.ToString(), "The application will now install the update and restart.",
                            "Update Available", DialogIcon.Ok);
                        db.ShowDialog();
                    }

                    if (doUpdate)
                    {
                        try
                        {
                            Mouse.OverrideCursor = Cursors.Wait;
                            ad.Update();
                            Mouse.OverrideCursor = Cursors.Arrow;
                            DialogBox db = new DialogBox("The application has been upgraded and needs to restart.",
                                "Save your workspace (if necessary) and restart the program.",
                                "Update", DialogIcon.Information);
                            db.ShowDialog();
                        }
                        catch (DeploymentDownloadException dde)
                        {
                            DialogBox db = new DialogBox("The new version of the application cannot be downloaded at this time. Please check your network connection, or try again later.",
                                dde.Message, "Update", DialogIcon.Error);
                            db.ShowDialog();

                            return;
                        }
                    }
                }
                else
                {
                    DialogBox db = new DialogBox("", "The application is up to date!", "Update", DialogIcon.Ok);
                    db.ShowDialog();
                    return;
                }
            }
            else
            {
                DialogBox db = new DialogBox("Update check failed because the application is not a ClickOnce application.",
                    "Only ClickOnce deployed applications can be updated.", "Update", DialogIcon.Error);
                db.ShowDialog();
                return;
            }
        }
        private void ribbonOpenInstallationFolder_Click(object sender, RoutedEventArgs e)
        {
            var location = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var folder = Path.GetDirectoryName(location);

            Process p = new Process();
            p.StartInfo = new ProcessStartInfo("explorer.exe", folder);
            p.Start();
        }

        #endregion

        #region IO
        private void CommandNew(object sender, ExecutedRoutedEventArgs e)
        {
            MainWindow mw = new MainWindow();
            mw.Show();
        }
        private void ribbonClearWorkspace_Click(object sender, RoutedEventArgs e)
        {
            if (!Workspace.HasContents) return;

            DialogBox db = new DialogBox("Are you sure you want to clear the current workspace?",
                "Click OK to delete all data or Cancel to return.",
                "Workspace", DialogIcon.Error, true);
            if (db.ShowDialog() == true)
            {
                Workspace = new Workspace();
                Workspace.InitializeRegistry();
            }
        }

        private void CommandOpen(object sender, ExecutedRoutedEventArgs e)
        {
            e.Handled = true;

            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Title = "Open Workspace";
            ofd.Filter = "Workspace Files (.wks)|*.wks";
            if (ofd.ShowDialog() != true) return;

            DoOpen(ofd.FileName);
        }
        private void DoOpen(string File)
        {
            bool merge = false;
            if (Workspace.HasContents)
            {
                WorkspaceDialog wd = new WorkspaceDialog();
                wd.ShowDialog();
                if (wd.DialogResult == false)
                {
                    return;
                }
                else
                {
                    WorkspaceResult result = wd.WorkspaceResult;
                    merge = result == WorkspaceResult.Merge;
                }
            }

            BackgroundWorker bw = new BackgroundWorker();
            bw.WorkerReportsProgress = true;
            bw.RunWorkerCompleted += bw_RunWorkerCompletedLoadWorkspace;
            bw.ProgressChanged += bw_ProgressChanged;
            bw.DoWork += bw_DoWorkLoadWorkspace;

            pw = new ProgressWindow("Loading " + System.IO.Path.GetFileName(File), "Load");
            pw.Show();

            //Args layout:
            //0: (string)File name
            //1: (bool)Merge
            //2: (bool)Hide load dialog
            object[] args = new object[3]
            {
                File, merge, Workspace.Registry.HideLoadDialog
            };

            bw.RunWorkerAsync(args);
        }
        private void bw_DoWorkLoadWorkspace(object sender, DoWorkEventArgs e)
        {
            //Args layout:
            //0: (string)File name
            //1: (bool)Merge
            //2: (bool)Hide load dialog
            object[] args = (object[])e.Argument;

            try
            {
                Workspace w = new Workspace((string)args[0], sender as BackgroundWorker);
                //Result layout:
                //0: (string)File name
                //1: (bool)Merge
                //2: (bool)Hide load dialog
                //3: (Workspace)Loaded workspace
                e.Result = new object[4] { args[0], args[1], args[2], w };
            }
            catch (Exception ex)
            {
                e.Result = ex;
            }

        }
        private void bw_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            pw.UpdateProgress(e.ProgressPercentage);
        }
        private void bw_RunWorkerCompletedLoadWorkspace(object sender, RunWorkerCompletedEventArgs e)
        {
            // Check if load failed due to exception
            Exception ex = e.Result as Exception;
            if (ex != null)
            {
                string message = ex.Message;
                if (ex.InnerException != null)
                {
                    message += ": " + ex.InnerException.Message;
                }

                pw.Close();
                DialogBox.Show("The workspace could not be loaded.",
                    message, "Load", DialogIcon.Error);
                return;
            }

            //Result layout:
            //0: (string)File name
            //1: (bool)Merge
            //2: (bool)Hide load dialog
            //3: (Workspace)Loaded workspace
            object[] result;
            try
            {
                result = (object[])e.Result;
            }
            catch (System.Reflection.TargetInvocationException)
            {
                pw.Close();
                DialogBox.Show("The workspace could not be loaded.",
                    "Most likely this is caused by an outdated workspace file version.", "Load", DialogIcon.Error);
                return;
            }


            Workspace w = (Workspace)result[3];

            //Need to recreate registry settings so that DependencyProperties are available to the main UI thread
            w.InitializeRegistry();

            if ((bool)result[1])
            {
                Workspace.Merge(w);
            }
            else
            {
                Workspace = w;
            }


            if ((bool)result[2]) pw.Close();
            else pw.ProgressFinished("Load complete!");

            BackgroundWorker bw = sender as BackgroundWorker;

            bw.RunWorkerCompleted -= bw_RunWorkerCompletedLoadWorkspace;
            bw.ProgressChanged -= bw_ProgressChanged;
            bw.DoWork -= bw_DoWorkLoadWorkspace;
            bw.Dispose();

            string file = (string)result[0];
            UpdateRecentFiles(file);

            StatusUpdated(this, new StatusUpdatedRoutedEventArgs(string.Format("Workspace {0} loaded.", w.WorkspaceName)));
        }

        private void CommandSave(object sender, ExecutedRoutedEventArgs e)
        {
            if (e != null)
            {
                e.Handled = true;
            }

            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "ImagingSIMS Workspace (.wks)|*.wks";
            Nullable<bool> result = sfd.ShowDialog();
            if (result != true) return;

            pw = new ProgressWindow("Saving " + System.IO.Path.GetFileName(sfd.FileName), "Save");
            pw.Show();

            BackgroundWorker bw = new BackgroundWorker();
            bw.WorkerReportsProgress = true;
            bw.RunWorkerCompleted += bw_RunWorkerCompletedSaveWorkspace;
            bw.ProgressChanged += bw_ProgressChanged;
            bw.DoWork += bw_DoWorkSaveWorkspace;

            object[] args = new object[2]
            {
                Workspace, sfd.FileName
            };

            bw.RunWorkerAsync(args);
        }
        private void bw_DoWorkSaveWorkspace(object sender, DoWorkEventArgs e)
        {
            //(Workspace) Workspace to save
            //(string) File name
            object[] args = (object[])e.Argument;
            Workspace workspace = (Workspace)args[0];
            string fileName = (string)args[1];

            string tempFile = fileName + ".temp";
            if (File.Exists(fileName))
            {
                File.Copy(fileName, tempFile);
            }

            workspace.Save(fileName, sender as BackgroundWorker);
            e.Result = fileName;

            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
        private void bw_RunWorkerCompletedSaveWorkspace(object sender, RunWorkerCompletedEventArgs e)
        {
            string file = (string)e.Result;

            pw.ProgressFinished("Save complete!");

            BackgroundWorker bw = sender as BackgroundWorker;

            bw.RunWorkerCompleted -= bw_RunWorkerCompletedSaveWorkspace;
            bw.ProgressChanged -= bw_ProgressChanged;
            bw.DoWork -= bw_DoWorkSaveWorkspace;
            bw.Dispose();

            StatusUpdated(this, new StatusUpdatedRoutedEventArgs(string.Format("Workspace {0} saved.", Workspace.WorkspaceName)));
            UpdateRecentFiles(file);
        }

        private async void LoadDataTable(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Multiselect = true;
            FileType fileType = FileType.BioToF;

            if (sender == ribbonButtonDataBioToF)
            {
                fileType = FileType.BioToF;
                ofd.Title = "Open Bio-ToF Data";
                ofd.Filter = "Text Files (.txt)|*.txt|XYI Images (.img)|*.img|All Files|*.*";
            }
            else if (sender == ribbonButtonDataJ105)
            {
                fileType = FileType.J105;
                ofd.Title = "Open J105 Data";
                ofd.Filter = "Text Files (.txt)|*.txt|All Files|*.*";
            }
            else if (sender == ribbonButtonDataCamecaAPM)
            {
                fileType = FileType.CamecaAPM;
                ofd.Title = "Open Cameca APM Data";
                ofd.Filter = "Text File (.txt)|*.txt|All Files |*.*";
            }
            else if (sender == ribbonButtonDataQStar)
            {
                fileType = FileType.QStar;

                DialogBox db = new DialogBox("Function not implemented.",
                "Currently, QStar data is not supported in ImagingSIMS. Please load data from another source.",
                "Open", DialogIcon.Information);
                db.ShowDialog();

                return;
            }
            else if (sender == ribbonButtonDataCSV)
            {
                fileType = FileType.CSV;
                ofd.Title = "Open CSV Data";
                ofd.Filter = "Comma Separated Values Files (.csv)|*.csv|Text Files (.txt)|*.txt|All Files|*.*";
            }
            else if (sender == ribbonButtonDataSeeker)
            {
                fileType = FileType.SmartSeekerData;
                ofd.Title = "Open SmartSeeker Data";
                ofd.Filter = "Test File (.txt)|*.txt";
            }
            else return;

            Nullable<bool> result = ofd.ShowDialog();
            if (result != true) return;

            int filesLoaded = 0;

            try
            {
                foreach (string s in ofd.FileNames)
                {
                    Data2D loaded = await Data2D.LoadData2DAsync(s, fileType);
                    Workspace.Data.Add(loaded);
                    filesLoaded++;
                }
            }
            catch (IOException IOex)
            {
                Mouse.OverrideCursor = Cursors.Arrow;
                DialogBox.Show(string.Format("Could not load the specified file: {0}", IOex.Message),
                    string.Format("Check to make sure you are loading data tables that match the specified file type ({0}).", fileType),
                    "Load", DialogIcon.Error);
                return;
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = Cursors.Arrow;
                string msg = ex.Message;
                if (ex.InnerException != null)
                {
                    msg += (" " + ex.InnerException.Message);
                }

                DialogBox.Show("Could not load the specified file.", msg, "Load", DialogIcon.Error);
                return;
            }

            StatusUpdated(this, new StatusUpdatedRoutedEventArgs(string.Format("{0} tables loaded.", filesLoaded)));
        }

        private void LoadSEM(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "SEM Image (.img)|*.img";
            ofd.Title = "Open SEM Image";
            if (ofd.ShowDialog() != true) return;

            Mouse.OverrideCursor = Cursors.Wait;

            SEM sem = new SEM(ofd.FileName);

            Workspace.SEMs.Add(sem);

            Mouse.OverrideCursor = Cursors.Arrow;

            StatusUpdated(this, new StatusUpdatedRoutedEventArgs(string.Format("SEM {0} loaded.", sem.SEMName)));
        }

        private void LoadSpec(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            if (Workspace.Registry.DefaultProgram == DefaultProgram.NotSpecified)
            {
                DialogBox.Show("No default program selected.",
                    "Please choose a default program in the Options tab and try again.", "Load", DialogIcon.Error);
                return;
            }

            var filter = SpectrumFileExtensions.GetFilterForDefaultProgram(Workspace.Registry.DefaultProgram);

            OpenFileDialog ofd = new OpenFileDialog();

            ofd.Title = "Open Spectra";
            ofd.Filter = filter;
            ofd.Multiselect = true;
            Nullable<bool> result = ofd.ShowDialog();
            if (result != true) return;

            LoadMSArguments args = new LoadMSArguments();

            string extension = Path.GetExtension(ofd.FileName).ToLower();

            var spectrumType = SpectrumFileExtensions.GetTypeForFileExtension(extension);

            if (spectrumType == SpectrumType.None)
            {
                throw new ArgumentException("Could not determine the spectrum type from the file extension");
            }

            if (spectrumType == SpectrumType.BioToF)
            {
                args.NumberFiles = ofd.FileNames.Length;
                args.FileNames = ofd.FileNames;
                args.Type = spectrumType;
            }
            else if (spectrumType == SpectrumType.J105)
            {
                if (ofd.FileNames.Length > 1)
                {
                    DialogBox db = new DialogBox("Only one J105 spectrum file can be loaded at a time.",
                        "Click OK to load the first selected spectrum or Cancel to return.", "Load", DialogIcon.Help, true);
                    Nullable<bool> r = db.ShowDialog();
                    if (r != true) return;
                }
                args.NumberFiles = 1;
                args.FileName = ofd.FileName;
                args.Type = spectrumType;
                args.SaveQuickLoadFile = Workspace.Registry.SaveQuickLoad;
            }
            else if (spectrumType == SpectrumType.Cameca1280)
            {
                args.NumberFiles = ofd.FileName.Length;
                args.FileNames = ofd.FileNames;
                args.Type = spectrumType;
            }
            else if (spectrumType == SpectrumType.CamecaNanoSIMS)
            {
                args.NumberFiles = ofd.FileName.Length;
                args.FileNames = ofd.FileNames;
                args.Type = spectrumType;
            }

            pw = new ProgressWindow("Loading " + System.IO.Path.GetFileName(ofd.FileName), "Load");
            pw.Show();

            BackgroundWorker bw = new BackgroundWorker();
            bw.WorkerReportsProgress = true;
            bw.RunWorkerCompleted += bw_RunWorkerCompletedSpectra;
            bw.ProgressChanged += bw_ProgressChanged;
            bw.DoWork += bw_DoWorkLoadSpectra;

            bw.RunWorkerAsync(args);
        }
        private void bw_DoWorkLoadSpectra(object sender, DoWorkEventArgs e)
        {
            LoadMSArguments args = (LoadMSArguments)e.Argument;

            if (args.Type == SpectrumType.J105)
            {
                try
                {
                    J105Spectrum j105spec = new J105Spectrum(System.IO.Path.GetFileNameWithoutExtension(args.FileName));
                    j105spec.LoadFromFile(args.FileName, sender as BackgroundWorker);

                    bool hasQuickLoad = j105spec.J105Stream.HasQuickLoadFile;

                    if (args.SaveQuickLoadFile && !hasQuickLoad) j105spec.SaveQuickLoad();

                    e.Result = j105spec;
                }
                catch (Exception ex)
                {
                    e.Result = ex;
                }
            }
            else if (args.Type == SpectrumType.BioToF)
            {
                try
                {
                    BioToFSpectrum btspec = new BioToFSpectrum(System.IO.Path.GetFileNameWithoutExtension(args.FileName));
                    btspec.LoadFromFile(args.FileNames, sender as BackgroundWorker);
                    e.Result = btspec;
                }
                catch (Exception ex)
                {
                    e.Result = ex;
                }
            }
            else if(args.Type == SpectrumType.Cameca1280)
            {
                try
                {
                    Cameca1280Spectrum camecaSpec = new Cameca1280Spectrum(System.IO.Path.GetFileNameWithoutExtension(args.FileName));
                    camecaSpec.LoadFromFile(args.FileNames, sender as BackgroundWorker);
                    e.Result = camecaSpec;
                }
                catch(Exception ex)
                {
                    e.Result = ex;
                }
            }
            else if(args.Type == SpectrumType.CamecaNanoSIMS)
            {
                try
                {
                    CamecaNanoSIMSSpectrum nanosimsSpec = new CamecaNanoSIMSSpectrum(Path.GetFileNameWithoutExtension(args.FileName));
                    nanosimsSpec.LoadFromFile(args.FileNames, sender as BackgroundWorker);
                    e.Result = nanosimsSpec;
                }
                catch(Exception ex)
                {
                    e.Result = ex;
                }
            }
        }
        private void bw_RunWorkerCompletedSpectra(object sender, RunWorkerCompletedEventArgs e)
        {
            if (Workspace.Registry.HideLoadDialog) pw.Close();
            else pw.ProgressFinished("Load complete!");

            BackgroundWorker bw = sender as BackgroundWorker;

            bw.RunWorkerCompleted -= bw_RunWorkerCompletedSpectra;
            bw.ProgressChanged -= bw_ProgressChanged;
            bw.DoWork -= bw_DoWorkLoadSpectra;
            bw.Dispose();

            Spectrum s = e.Result as Spectrum;
            if (s != null) Workspace.Spectra.Add(s);
            else
            {
                string message = "";
                string inner = "";
                Exception ex = e.Result as Exception;

                message = ex.Message;
                if (ex.InnerException != null) inner = ex.InnerException.Message;

                pw.Close();
                pw = null;

                DialogBox db = new DialogBox(message, inner, "Load", DialogIcon.Error);
                db.ShowDialog();

                return;
            }

            StatusUpdated(this, new StatusUpdatedRoutedEventArgs(string.Format("Spectrum {0} loaded.", s.Name)));
        }

        private void LoadImageSeries(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Title = "Open Image Series";
            ofd.Filter = "Image Files (.bmp, .jpg, .jpeg, .png, .tif, .tiff)|*.bmp;*.jpg;*.jpeg;*.png;*.tif;*.tiff|" +
                            "Bitmap Images (.bmp)|*.bmp|JPEG Images (.jpg, .jpeg)|*.jpg;*.jpeg|PNG Images (.png)|*.png|Tiff Images (.tif, .tiff)|*.tif;*.tiff";
            ofd.Multiselect = true;
            if (ofd.ShowDialog() != true) return;

            DisplayImage[] images = new DisplayImage[ofd.FileNames.Length];
            string[] titles = new string[ofd.FileNames.Length];

            for (int i = 0; i < ofd.FileNames.Length; i++)
            {
                DisplayImage image = new DisplayImage();
                BitmapImage src = new BitmapImage();

                src.BeginInit();
                src.UriSource = new Uri(ofd.FileNames[i], UriKind.Absolute);
                src.CacheOption = BitmapCacheOption.OnLoad;
                src.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                src.EndInit();

                image.Source = src;
                image.Stretch = Stretch.Uniform;

                image.Title = System.IO.Path.GetFileNameWithoutExtension(ofd.FileNames[i]);

                images[i] = image;

            }

            DisplaySeries series = new DisplaySeries(images);
            series.SeriesName = System.IO.Path.GetFileNameWithoutExtension(ofd.FileName);
            Workspace.ImageSeries.Add(series);

            DisplayTab it = new DisplayTab();
            it.CurrentSeries = series;
            ClosableTabItem cti = ClosableTabItem.Create(it, TabType.Display, series.SeriesName);
            tabMain.Items.Add(cti);
            tabMain.SelectedItem = cti;

            StatusUpdated(this, new StatusUpdatedRoutedEventArgs("Load complete."));
        }
        private void LoadSampleData(object sender, RoutedEventArgs e)
        {
            e.Handled = true;

            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Title = "Open Sample Data";
            ofd.Filter = "ImagingSIMS Sample Data Files (.isd)|*.isd";
            if (ofd.ShowDialog() != true) return;

            SampleData sd = new SampleData(ofd.FileName);
            foreach (Data2D d in sd.SphereData.Layers)
            {
                Workspace.Data.Add(d);
            }

            StatusUpdated(this, new StatusUpdatedRoutedEventArgs(
                string.Format("Sample data {0} loaeded.", Path.GetFileName(ofd.FileName))));
        }

        private void SaveImageSeries(object sender, RoutedEventArgs e)
        {
            ClosableTabItem cti = tabMain.SelectedItem as ClosableTabItem;
            if (cti == null)
            {
                DialogBox db = new DialogBox("Image series not selected.", "Please navigate to an Image Display tab and try again.",
                    "Images", DialogIcon.Error);
                db.ShowDialog();
                return;
            }

            TabType t = cti.TabType;

            ImageTabEvent eventType = ImageTabEvent.Save;

            switch (t)
            {
                case TabType.Display:
                    DisplayTab it = (DisplayTab)cti.Content;
                    if (it != null) it.CallEvent(eventType);
                    break;
                case TabType.SEM:
                    SEMTab st = (SEMTab)cti.Content;
                    if (st != null) st.CallEvent(eventType);
                    break;
                case TabType.Fusion:
                    FusionTab ft = (FusionTab)cti.Content;
                    if (ft != null) ft.CallEvent(eventType);
                    break;
                case TabType.DataDisplay:
                    DataDisplayTab dt = (DataDisplayTab)cti.Content;
                    if (dt != null) dt.SaveImageSeries();
                    break;
                default:
                    DialogBox db = new DialogBox("Image series not selected.", "Please navigate to an Image Display tab and try again.",
                       "Images", DialogIcon.Error);
                    db.ShowDialog();
                    return;
            }
        }
        private void SaveImageData(object sender, RoutedEventArgs e)
        {
        }

        private void recentItem_FileClicked(object sender, RoutedEventArgs e)
        {
            FileItem fi = sender as FileItem;
            if (fi == null) return;

            string file = fi.FilePath;

            if (!File.Exists(file))
            {
                DialogBox db = new DialogBox(string.Format("The file {0} does not exist in the target location.", System.IO.Path.GetFileName(file)),
                    "Click OK to remove the file from the recent list, or click Cancel to return.", "Recent Files", DialogIcon.Help, true);
                Nullable<bool> result = db.ShowDialog();

                if (result == true)
                {
                    Workspace.Registry.RecentFiles.Remove(file);
                }

                return;
            }

            DoOpen(file);
        }
        private void recentItem_RemoveFileClicked(object sender, RoutedEventArgs e)
        {
            FileItem fi = sender as FileItem;
            if (fi == null) return;

            string file = fi.FilePath;

            DialogBox db = new DialogBox(string.Format("Are you sure you wish to remove {0} from the recent files list?", file),
                "Click OK to remove the file from the recent list, or click Cancel to return.", "Recent Files", DialogIcon.Help, true);
            if (db.ShowDialog() != true) return;

            Workspace.Registry.RecentFiles.Remove(file);
        }
        private void ribbonOpenRecentFile_Click(object sender, RoutedEventArgs e)
        {
            string file = String.Empty;
            if (sender is RibbonApplicationMenuItem)
            {
                file = (string)((RibbonApplicationMenuItem)sender).ToolTip;
            }
            else if (sender is RibbonButton)
            {
                file = (string)((RibbonButton)sender).ToolTip;
            }
            else return;

            if (file == String.Empty) return;

            if (!File.Exists(file))
            {
                DialogBox db = new DialogBox(string.Format("The file {0} does not exist in the target location.", System.IO.Path.GetFileName(file)),
                    "Click OK to remove the file from the recent list, or click Cancel to return.", "Recent Files", DialogIcon.Help, true);
                Nullable<bool> result = db.ShowDialog();

                if (result == true)
                {
                    Workspace.Registry.RecentFiles.Remove(file);
                }

                return;
            }

            DoOpen(file);
        }
        private void UpdateRecentFiles(string FilePath)
        {
            Workspace.Registry.RecentFiles.Add(FilePath);
        }
        private void ribbonRecentRemove_Click(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem;

            RibbonApplicationMenuItem rami = null;
            if (mi != null)
            {
                rami = ((ContextMenu)mi.Parent).PlacementTarget as RibbonApplicationMenuItem;

                if (rami != null)
                {
                    string file = (string)rami.ToolTip;

                    DialogBox db = new DialogBox(string.Format("Are you sure you wish to remove {0} from the recent files list?", file),
                        "Click OK to remove the file from the recent list, or click Cancel to return.", "Recent Files", DialogIcon.Help, true);
                    if (db.ShowDialog() != true) return;

                    Workspace.Registry.RecentFiles.Remove(file);
                }
                else
                {
                    RibbonButton rb = ((ContextMenu)mi.Parent).PlacementTarget as RibbonButton;

                    if (rb != null)
                    {
                        string file = (string)rb.ToolTip;

                        DialogBox db = new DialogBox(string.Format("Are you sure you wish to remove {0} from the recent files list?", file),
                            "Click OK to remove the file from the recent list, or click Cancel to return.", "Recent Files", DialogIcon.Help, true);
                        if (db.ShowDialog() != true) return;

                        Workspace.Registry.RecentFiles.Remove(file);
                    }
                }
            }
        }
        private void ribbonRecentOpenLocation_Click(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem;

            RibbonApplicationMenuItem rami = null;
            if (mi != null)
            {
                rami = ((ContextMenu)mi.Parent).PlacementTarget as RibbonApplicationMenuItem;
                string file = "";

                if (rami != null)
                {
                    file = (string)rami.ToolTip;
                }
                else
                {
                    RibbonButton rb = ((ContextMenu)mi.Parent).PlacementTarget as RibbonButton;

                    if (rb != null)
                    {
                        file = (string)rb.ToolTip;
                    }
                }

                if (file == "")
                {
                    DialogBox db = new DialogBox("Could not determine the location of the recent item.",
                        "Verify the recent item still exists and try again.", "Recent File", DialogIcon.Error);
                    db.ShowDialog();
                    return;
                }

                if (!File.Exists(file))
                {
                    DialogBox db = new DialogBox("Could not find the file associated with the recent item.",
                        "Verify the recent item still exists and try again. If it does not, consider removing it from the Recent File List.",
                        "Recent File", DialogIcon.Error);
                    db.ShowDialog();
                    return;
                }
                Process p = new Process();
                p.StartInfo.FileName = @"C:\Windows\explorer.exe";
                p.StartInfo.Arguments = @"/select, " + file;
                p.Start();
            }
        }
        #endregion

        #region Commands
        public static RoutedCommand SpecBackCommand = new RoutedCommand();
        public static RoutedCommand SpecForwardCommand = new RoutedCommand();
        public static RoutedCommand SpecResetCommand = new RoutedCommand();

        private void InitializeCommands()
        {
            Trace.WriteLine("Initializing commands.");

            CommandBinding bindingNew = new CommandBinding(ApplicationCommands.New);
            bindingNew.Executed += CommandNew;
            this.CommandBindings.Add(bindingNew);

            CommandBinding bindingOpen = new CommandBinding(ApplicationCommands.Open);
            bindingOpen.Executed += CommandOpen;
            this.CommandBindings.Add(bindingOpen);

            CommandBinding bindingSave = new CommandBinding(ApplicationCommands.Save);
            bindingSave.Executed += CommandSave;
            this.CommandBindings.Add(bindingSave);

            CommandBinding bindingUndo = new CommandBinding(ApplicationCommands.Undo);
            bindingUndo.Executed += CommandUndo;
            bindingUndo.CanExecute += CanExecuteUndo;
            this.CommandBindings.Add(bindingUndo);

            CommandBinding bindingRedo = new CommandBinding(ApplicationCommands.Redo);
            bindingRedo.Executed += CommandRedo;
            bindingRedo.CanExecute += CanExecuteRedo;
            this.CommandBindings.Add(bindingRedo);

            CommandBinding bindingClose = new CommandBinding(ApplicationCommands.Close);
            bindingClose.Executed += CommandClose;
            this.CommandBindings.Add(bindingClose);

            CommandBinding bindingPrintPreview = new CommandBinding(ApplicationCommands.PrintPreview);
            bindingPrintPreview.Executed += CommandPrintPreview;
            this.CommandBindings.Add(bindingPrintPreview);

            CommandBinding bindingPrint = new CommandBinding(ApplicationCommands.Print);
            bindingPrint.Executed += CommandPrint;
            this.CommandBindings.Add(bindingPrint);

            CommandBinding bindingSpecBack = new CommandBinding(SpecBackCommand);
            bindingSpecBack.Executed += CommandSpecBack;
            bindingSpecBack.CanExecute += CanExecuteSpecBack;
            this.CommandBindings.Add(bindingSpecBack);

            CommandBinding bindingSpecForward = new CommandBinding(SpecForwardCommand);
            bindingSpecForward.Executed += CommandSpecForward;
            bindingSpecForward.CanExecute += CanExecuteSpecForward;
            this.CommandBindings.Add(bindingSpecForward);

            CommandBinding bindingSpecReset = new CommandBinding(SpecResetCommand);
            bindingSpecReset.Executed += CommandSpecReset;
            bindingSpecReset.CanExecute += CanExecuteSpecReset;
            this.CommandBindings.Add(bindingSpecReset);

            Trace.WriteLine("Commands initialized and bound.");
        }

        private void CanExecuteUndo(object sender, CanExecuteRoutedEventArgs e)
        {
            if (tabMain.SelectedItem == null)
            {
                e.CanExecute = false;
                return;
            }

            ClosableTabItem cti = (ClosableTabItem)tabMain.SelectedItem;
            if (cti == null)
            {
                e.CanExecute = false;
                return;
            }

            e.CanExecute = cti.CanUndo;
        }
        private void CanExecuteRedo(object sender, CanExecuteRoutedEventArgs e)
        {
            if (tabMain.SelectedItem == null)
            {
                e.CanExecute = false;
                return;
            }

            ClosableTabItem cti = (ClosableTabItem)tabMain.SelectedItem;
            if (cti == null)
            {
                e.CanExecute = false;
                return;
            }

            e.CanExecute = cti.CanRedo;
        }
        private void CommandUndo(object sender, ExecutedRoutedEventArgs e)
        {
            if (tabMain.SelectedItem == null) return;
            ClosableTabItem cti = (ClosableTabItem)tabMain.SelectedItem;
            if (cti == null) return;

            cti.Undo();
        }
        private void CommandRedo(object sender, ExecutedRoutedEventArgs e)
        {
            if (tabMain.SelectedItem == null) return;
            ClosableTabItem cti = (ClosableTabItem)tabMain.SelectedItem;
            if (cti == null) return;

            cti.Redo();
        }

        private void RibbonWindow_Closing(object sender, CancelEventArgs e)
        {
            ProgressWindowManager.DisposeAll();
            ImagingSIMS.ImageRegistration.ImageOverlayWindowManager.DisposeAll();

            if (tlw != null) tlw.Close();
            ApplicationCommands.Close.Execute(this, null);
        }
        private void RibbonWindow_Unloaded(object sender, RoutedEventArgs e)
        {
            ProgressWindowManager.DisposeAll();
            ImagingSIMS.ImageRegistration.ImageOverlayWindowManager.DisposeAll();

            if (tlw != null) tlw.Close();
            ApplicationCommands.Close.Execute(this, null);
        }
        private void CommandClose(object sender, ExecutedRoutedEventArgs e)
        {
            Workspace.Registry.SaveSettings();

            if (Workspace.Registry.ClearPluginData)
            {
                var fileInfo = new DirectoryInfo(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"ImagingSIMS\plugins"));
                fileInfo.GetFiles("*", SearchOption.AllDirectories).ToList().ForEach(file => file.Delete());
            }

            //Closes the application with no further action if the application is in Debug mode
            if (IsDebug)
            {
                return;
            }

            if (!Workspace.IsDirty) return;

            DialogBox db = new DialogBox("The current workspace has been modified. Do you wish to save changes?",
                      "Click OK to save or Cancel to quit.", "Close", DialogIcon.Help, true);
            Nullable<bool> result = db.ShowDialog();
            if (result == true)
            {
                CommandSave(this, null);
            }
        }

        private void CommandPrintPreview(object sender, ExecutedRoutedEventArgs e)
        {
            TabItem ti = tabMain.SelectedItem as TabItem;
            if (ti == null) return;

            ClosableTabItem cti = ti as ClosableTabItem;
            if (cti == null) return;

            TabType tabType = cti.TabType;

            if (tabType != TabType.Spectrum) return;

        }
        private void CommandPrint(object sender, ExecutedRoutedEventArgs e)
        {
            TabItem ti = tabMain.SelectedItem as TabItem;
            if (ti == null) return;

            ClosableTabItem cti = ti as ClosableTabItem;
            if (cti == null) return;

            TabType tabType = cti.TabType;

            PrintDialog pd = new PrintDialog();
            Nullable<bool> result = pd.ShowDialog();

            if (result != true) return;

            UserControl control = cti.Content as UserControl;
            pd.PrintVisual(control, cti.Header.ToString());

            //if (tabType != TabType.Spectrum) return;

            //switch (tabType)
            //{
            //    case TabType.Spectrum:
            //        SpectrumTab st = cti.Content as SpectrumTab;
            //        if (st == null) return;
            //        st.Print();
            //        break;
            //}
        }

        private void CanExecuteSpecBack(object sender, CanExecuteRoutedEventArgs e)
        {
            if (tabMain.SelectedItem == null)
            {
                e.CanExecute = false;
                return;
            }

            ClosableTabItem cti = (ClosableTabItem)tabMain.SelectedItem;
            if (cti == null)
            {
                e.CanExecute = false;
                return;
            }

            SpectrumTab st = cti.Content as SpectrumTab;
            if (st == null)
            {
                e.CanExecute = false;
                return;
            }

            e.CanExecute = st.CanHistoryBack;
        }
        private void CommandSpecBack(object sender, ExecutedRoutedEventArgs e)
        {
            SpectrumTab st = ((ClosableTabItem)tabMain.SelectedItem).Content as SpectrumTab;
            st.HistoryBack();
        }
        private void CanExecuteSpecForward(object sender, CanExecuteRoutedEventArgs e)
        {
            if (tabMain.SelectedItem == null)
            {
                e.CanExecute = false;
                return;
            }

            ClosableTabItem cti = (ClosableTabItem)tabMain.SelectedItem;
            if (cti == null)
            {
                e.CanExecute = false;
                return;
            }

            SpectrumTab st = cti.Content as SpectrumTab;
            if (st == null)
            {
                e.CanExecute = false;
                return;
            }

            e.CanExecute = st.CanHistoryForward;
        }
        private void CommandSpecForward(object sender, ExecutedRoutedEventArgs e)
        {
            SpectrumTab st = ((ClosableTabItem)tabMain.SelectedItem).Content as SpectrumTab;
            st.HistoryForward();
        }
        private void CanExecuteSpecReset(object sender, CanExecuteRoutedEventArgs e)
        {
            if (tabMain.SelectedItem == null)
            {
                e.CanExecute = false;
                return;
            }

            ClosableTabItem cti = (ClosableTabItem)tabMain.SelectedItem;
            if (cti == null)
            {
                e.CanExecute = false;
                return;
            }

            SpectrumTab st = cti.Content as SpectrumTab;
            if (st == null)
            {
                e.CanExecute = false;
                return;
            }

            // Can always reset spectrum to initial view if tab is a spectrum
            e.CanExecute = true;
        }
        private void CommandSpecReset(object sender, ExecutedRoutedEventArgs e)
        {
            SpectrumTab st = ((ClosableTabItem)tabMain.SelectedItem).Content as SpectrumTab;
            st.Reset();
        }
        #endregion

        #region Ribbon Interactions
        private void ribbonEditWorkspace_Click(object sender, RoutedEventArgs e)
        {
            var editTab = new ManageWorkspaceTab(Workspace);
            var cti = ClosableTabItem.Create(editTab, TabType.EditWorkspace, "Edit Workspace");
            AddTabItemAndNavigate(cti);
        }

        private void ribbonOpenSumTab_Click(object sender, RoutedEventArgs e)
        {
            TableSumTab tst = new TableSumTab();
            ClosableTabItem cti = ClosableTabItem.Create(tst, TabType.TableSelector, "Sum");
            tabMain.Items.Add(cti);
            tabMain.SelectedItem = cti;
        }
        private void ribbonOpenCropTab_Click(object sender, RoutedEventArgs e)
        {
            CropTab ct = new CropTab();
            ClosableTabItem cti = ClosableTabItem.Create(ct, TabType.Crop, "Crop");
            tabMain.Items.Add(cti);
            tabMain.SelectedItem = cti;
        }
        private void ribbonOpenCorrectionTab_Click(object sender, RoutedEventArgs e)
        {
            DataCorrectionTab dct = new DataCorrectionTab();
            ClosableTabItem cti = ClosableTabItem.Create(dct, TabType.Correction, "Data Correction");
            tabMain.Items.Add(cti);
            tabMain.SelectedItem = cti;
        }
        private void ribbonOpenZCorrectionTab_Click(object sender, RoutedEventArgs e)
        {
            ZCorrectionTab zct = new ZCorrectionTab();
            ClosableTabItem cti = ClosableTabItem.Create(zct, TabType.ZCorrection, "Z-Correction");
            tabMain.Items.Add(cti);
            tabMain.SelectedItem = cti;

        }
        private void ribbonOpenDepthProfileTab_Click(object sender, RoutedEventArgs e)
        {
            DepthProfileTab dpt = new DepthProfileTab();
            ClosableTabItem cti = ClosableTabItem.Create(dpt, TabType.DepthProfile, "Depth Profile");
            tabMain.Items.Add(cti);
            tabMain.SelectedItem = cti;
        }
        private void ribbonOpenRatioTab_Click(object sender, RoutedEventArgs e)
        {
            RatioTab rt = new RatioTab();
            ClosableTabItem cti = ClosableTabItem.Create(rt, TabType.Ratio, "Ratio");
            tabMain.Items.Add(cti);
            tabMain.SelectedItem = cti;
        }
        private void ribbonOpenStitchTab_Click(object sender, RoutedEventArgs e)
        {
            ImageStitchTab ist = new ImageStitchTab();
            ist.Workspace = Workspace;

            ClosableTabItem cti = ClosableTabItem.Create(ist, TabType.ImageStitch, "Image Stitch");
            tabMain.Items.Add(cti);
            tabMain.SelectedItem = cti;
        }
        private void ribbonOpenDataMathTab_Click(object sender, RoutedEventArgs e)
        {
            var dmt = new DataMathTab();
            var cti = ClosableTabItem.Create(dmt, TabType.DataMath, "Data Math");
            AddTabItemAndNavigate(cti);
        }
        private void NewSampleData(object sender, RoutedEventArgs e)
        {
            SampleData sd = new SampleData(Workspace.SampleXDimension, Workspace.SampleYDimension, Workspace.SampleZDimension);
            if (Workspace.SampleDoMultiple)
            {
                sd.CreateSpheres(new int[2] { Workspace.SampleCenterX1, Workspace.SampleCenterX2 },
                    new int[2] { Workspace.SampleCenterY1, Workspace.SampleCenterY2 },
                    new int[2] { Workspace.SampleCenterZ1, Workspace.SampleCenterZ2 },
                    new int[2] { Workspace.SampleRadius1, Workspace.SampleRadius2 });
            }
            else
            {
                sd.CreateSphere(Workspace.SampleCenterX1, Workspace.SampleCenterY1, Workspace.SampleCenterZ1, Workspace.SampleRadius1);
            }
            foreach (Data2D d in sd.SphereData.Layers)
            {
                Workspace.Data.Add(d);
            }
        }

        private void showStartup_Click(object sender, RoutedEventArgs e)
        {
            if (tabMain.Items.Count > 0)
            {
                foreach (ClosableTabItem c in tabMain.Items)
                {
                    if (c.TabType == TabType.Startup)
                    {
                        tabMain.SelectedItem = c;
                        return;
                    }
                }
            }
            StartupTab st = new StartupTab();
            st.RecentFileClicked += startupTab_RecentFileClicked;
            st.RecentFileRemoveClicked += startupTab_RecentFileClicked;
            ClosableTabItem cti = ClosableTabItem.Create(st, TabType.Startup);
            tabMain.Items.Add(cti);
            tabMain.SelectedItem = cti;
        }

        private void startupTab_RecentFileClicked(object sender, RoutedEventArgs e)
        {
            FileItem fi = e.OriginalSource as FileItem;
            if (fi == null) return;

            string file = fi.FilePath;

            if (!File.Exists(file))
            {
                DialogBox db = new DialogBox(string.Format("The file {0} does not exist in the target location.", System.IO.Path.GetFileName(file)),
                    "Click OK to remove the file from the recent list, or click Cancel to return.", "Recent Files", DialogIcon.Help, true);
                Nullable<bool> result = db.ShowDialog();

                if (result == true)
                {
                    Workspace.Registry.RecentFiles.Remove(file);
                }

                return;
            }

            DoOpen(file);
        }
        private void startupTab_RecentFileRemoveClicked(object sender, RoutedEventArgs e)
        {
            FileItem fi = e.OriginalSource as FileItem;
            if (fi == null) return;

            string file = fi.FilePath;

            DialogBox db = new DialogBox(string.Format("Are you sure you wish to remove {0} from the recent files list?", file),
                "Click OK to remove the file from the recent list, or click Cancel to return.", "Recent Files", DialogIcon.Help, true);
            if (db.ShowDialog() != true) return;

            Workspace.Registry.RecentFiles.Remove(file);
        }

        private void CreateSampleData(object sender, RoutedEventArgs e)
        {
            SampleData sd = new SampleData(Workspace.SampleXDimension, Workspace.SampleYDimension, Workspace.SampleZDimension);
            if (Workspace.SampleDoMultiple)
            {
                sd.CreateSpheres(new int[2] { Workspace.SampleCenterX1, Workspace.SampleCenterX2 },
                    new int[2] { Workspace.SampleCenterY1, Workspace.SampleCenterY2 },
                    new int[2] { Workspace.SampleCenterZ1, Workspace.SampleCenterZ2 },
                    new int[2] { Workspace.SampleRadius1, Workspace.SampleRadius2 });
            }
            else
            {
                sd.CreateSphere(Workspace.SampleCenterX1, Workspace.SampleCenterY1,
                    Workspace.SampleCenterZ1, Workspace.SampleRadius1);
            }

            foreach (Data2D d in sd.SphereData.Layers)
            {
                Workspace.Data.Add(d);
            }

            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "ImagingSIMS Sample Data Files (.isd)|*.isd";
            Nullable<bool> result = sfd.ShowDialog();
            if (result != true) return;

            sd.Save(sfd.FileName);
        }

        private void NewComponent(object sender, RoutedEventArgs e)
        {
            ClosableTabItem cti = ClosableTabItem.Create(new ComponentTab(), TabType.Component, "New Component");
            tabMain.Items.Add(cti);
            tabMain.SelectedItem = cti;
        }
        private void ComponentCreated(object sender, RoutedEventArgs e)
        {
            Workspace.Components.Add(((ComponentTab)e.Source).NewComponent);

            ComponentTab ct = e.Source as ComponentTab;
            if (ct == null) return;

            ClosableTabItem cti = tabMain.SelectedItem as ClosableTabItem;
            if (cti == null) return;

            if(cti.Content == ct)
            {
                tabMain.Items.Remove(cti);
                ct = null;
            }
        }
        private void ComponentUpdated(object sender, RoutedEventArgs e)
        {
            int originalIndex = Workspace.Components.IndexOf(((ComponentTab)e.Source).OriginalComponent);
            if (originalIndex == -1)
            {
                Workspace.Components.Add(((ComponentTab)e.Source).NewComponent);
                throw new ArgumentException("Could not find original component in collection. Adding built component as a new component.");
            }
            Workspace.Components.RemoveAt(originalIndex);
            Workspace.Components.Insert(originalIndex, ((ComponentTab)e.Source).NewComponent);
        }

        private void DoCrossCorrelation(object sender, RoutedEventArgs e)
        {
            //ClosableTabItem cti = tabMain.SelectedItem as ClosableTabItem;
            //if (cti == null) return;

            //DisplayTab dt = cti.Content as DisplayTab;
            //if (dt == null) return;

            //BitmapSource img1 = dt.CurrentSeries.Images[0].Source as BitmapSource;
            //BitmapSource img2 = dt.CurrentSeries.Images[1].Source as BitmapSource;

            //Data.Analysis.CrossCorrelationResults results = Data.Analysis.CrossCorrelation.Analyze(img1, img2);

            //string message = string.Format(
            //    "Results\n" +
            //    "---------------------------\n" +
            //    "ccRed =    {0}\n" +
            //    "ccGreen =  {1}\n" +
            //    "ccBlue =   {2}\n" +
            //    "ccAvg =    {3}\n",
            //    results.R.ToString("0.0000"),
            //    results.G.ToString("0.0000"),
            //    results.B.ToString("0.0000"),
            //    ((results.R + results.G + results.B) / 3).ToString("0.0000"));

            //DialogBox db = new DialogBox("Cross correlation analysis complete!", message, "Cross Correlation", DialogBoxIcon.GreenCheck);
            //db.ShowDialog();
        }
        private void CreateImage(object sender, RoutedEventArgs e)
        {
            int numberComps = listBoxComponents.SelectedItems.Count;
            if (numberComps == 0)
            {
                DialogBox db = new DialogBox("No image components selected.",
                    "Use the drop down menu to select one or more components to create the image series.",
                    "Create Image", DialogIcon.Error);
                db.ShowDialog();
                return;
            }

            ImageComponent[] comps = new ImageComponent[numberComps];
            for (int i = 0; i < numberComps; i++)
            {
                comps[i] = (ImageComponent)listBoxComponents.SelectedItems[i];
            }

            BitmapSource[] bitmapSources = ImageGenerator.Instance.Create(comps, new ImagingParameters()
            {
                NormalizationMethod = (NormalizationMethod)comboNorm.SelectedItem,
                SqrtEnhance = checkSqrt.IsChecked == true,
                TotalIon = checkTotalIon.IsChecked == true
            });

            DisplayImage[] images = new DisplayImage[bitmapSources.Length];
            for (int i = 0; i < bitmapSources.Length; i++)
            {
                images[i] = new DisplayImage();
                images[i].Source = bitmapSources[i];

                string title = "";
                foreach (object obj in listBoxComponents.SelectedItems)
                {
                    title += ((ImageComponent)obj).ComponentName + "-";
                }
                title = title.Remove(title.Length - 1);
                title += "_" + i.ToString();
                images[i].Title = title;
            }

            DisplaySeries series = new DisplaySeries(images);
            string seriesName = "";
            foreach (object obj in listBoxComponents.SelectedItems)
            {
                seriesName += ((ImageComponent)obj).ComponentName + "-";
            }
            seriesName = seriesName.Remove(seriesName.Length - 1);
            series.SeriesName = seriesName;
            Workspace.ImageSeries.Add(series);

            DisplayTab it = new DisplayTab();
            it.CurrentSeries = series;

            ClosableTabItem cti = ClosableTabItem.Create(it, TabType.Display, it.CurrentSeries.SeriesName);
            tabMain.Items.Add(cti);
            tabMain.SelectedItem = cti;
        }
        private void NewOverlay(object sender, RoutedEventArgs e)
        {
            ClosableTabItem cti = ClosableTabItem.Create(new DisplayTab(), TabType.Display, "Image Display");
            tabMain.Items.Add(cti);
            tabMain.SelectedItem = cti;
        }
        private void OverlayAddImage(object sender, RoutedEventArgs e)
        {
            ClosableTabItem cti = (ClosableTabItem)tabMain.SelectedItem;
            if (cti.TabType != TabType.Display)
            {
                DialogBox db = new DialogBox("Selected tab not an overlay document.", "Open a new or navigate to an existing overlay document to add an image.",
                    "Overlay", DialogIcon.Error);
                db.ShowDialog();
                return;
            }

            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "Bitmap Images (.bmp)|*.bmp";
            ofd.Multiselect = true;
            Nullable<bool> result = ofd.ShowDialog();
            if (result != true) return;

            DisplayTab ot = (DisplayTab)cti.Content;
            if (ot == null)
            {
                DialogBox db = new DialogBox("Selected tab not an overlay document.", "Open a new or navigate to an existing overlay document to add an image.",
                    "Overlay", DialogIcon.Error);
                db.ShowDialog();
                return;
            }

            foreach (string s in ofd.FileNames)
            {
                BitmapImage src = new BitmapImage();
                src.BeginInit();
                src.UriSource = new Uri(s, UriKind.Absolute);
                src.CacheOption = BitmapCacheOption.OnLoad;
                src.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                src.EndInit();

                DisplayImage image = new DisplayImage();
                image.Source = src;
                image.Title = System.IO.Path.GetFileNameWithoutExtension(s);

                ot.AddImage(image);
            }
        }
        private void DoOverlay(object sender, RoutedEventArgs e)
        {
            ClosableTabItem cti = (ClosableTabItem)tabMain.SelectedItem;

            if (cti.TabType == TabType.Display)
            {
                DisplayTab dt = (DisplayTab)cti.Content;
                if (dt == null)
                {
                    DialogBox.Show("Selected tab not an image document which supports overlaying.",
                        "Open a new or navigate to an existing overlay document to add an image.",
                        "Overlay", DialogIcon.Error);
                    return;
                }
                dt.CallEvent(ImageTabEvent.Overlay);
            }
            else if (cti.TabType == TabType.DataDisplay)
            {
                DataDisplayTab d2dt = cti.Content as DataDisplayTab;
                if (d2dt == null)
                {
                    DialogBox.Show("Selected tab not an image document which supports overlaying.",
                        "Open a new or navigate to an existing overlay document to add an image.",
                        "Overlay", DialogIcon.Error);
                    return;
                }
                BitmapSource overlay = d2dt.GetOverlay();
                if (overlay == null) return;

                string name = "Overlay";
                DisplaySeries series = new DisplaySeries(new DisplayImage[1] { new DisplayImage(overlay, name) });
                series.SeriesName = name;
                Workspace.ImageSeries.Add(series);

                DisplayTab dt = new DisplayTab();
                dt.CurrentSeries = series;

                ClosableTabItem cti2 = ClosableTabItem.Create(dt, TabType.Display, series.SeriesName);
                tabMain.Items.Add(cti2);
                tabMain.SelectedItem = cti2;

                StatusUpdated(this, new StatusUpdatedRoutedEventArgs("Overlay complete."));
            }
            else
            {
                DialogBox.Show("Selected tab not an image document which supports overlaying.",
                        "Open a new or navigate to an existing overlay document to add an image.",
                        "Overlay", DialogIcon.Error);
                return;
            }
        }
        private void ribbonButtonExpandPixels_Click(object sender, RoutedEventArgs e)
        {
            ClosableTabItem cti = tabMain.SelectedItem as ClosableTabItem;
            if (cti == null) return;

            DataDisplayTab d2dt = cti.Content as DataDisplayTab;
            if (d2dt == null) return;

            int windowSize = 0;
            if (!int.TryParse(tbExpandPixelsWindowSize.Text, out windowSize))
            {
                DialogBox.Show("No window size specified.",
                    "Please enter a positive integer value for the window size parameter and try again.",
                    "Expand", DialogIcon.Error);
                return;
            }

            if (windowSize < 0)
            {
                DialogBox.Show("Invalid window size specified.",
                    "Please enter a positive integer value for the window size parameter and try again.",
                    "Expand", DialogIcon.Error);
                return;
            }

            d2dt.ExpandPixels(windowSize);
        }

        private void ApplyImageFilter(object sender, RoutedEventArgs e)
        {
            ClosableTabItem cti = tabMain.SelectedItem as ClosableTabItem;
            if (cti == null)
            {
                DialogBox db = new DialogBox("Image series not selected.", "Please navigate to an Image Display tab and try again.",
                    "Images", DialogIcon.Error);
                db.ShowDialog();
                return;
            }
            DisplayTab it = cti.Content as DisplayTab;
            if (it == null)
            {
                DialogBox db = new DialogBox("Image series not selected.", "Please navigate to an Image Display tab and try again.",
                      "Images", DialogIcon.Error);
                db.ShowDialog();
                return;
            }

            ImageTabEvent eventType = ImageTabEvent.Empty;

            if (e.Source == ribbonButtonHighFilter)
            {
                eventType = ImageTabEvent.FilterHighPass;
            }
            else if (e.Source == ribbonButtonLowFilter)
            {
                eventType = ImageTabEvent.FilterLowPass;
            }
            else if (e.Source == ribbonButtonMeanFilter)
            {
                eventType = ImageTabEvent.FilterMean;
            }
            else if (e.Source == ribbonButtonMedianFilter)
            {
                eventType = ImageTabEvent.FilterMedian;
            }
            else if (e.Source == ribbonButtonGaussianFilter)
            {
                eventType = ImageTabEvent.FilterGaussian;
            }

            it.CallEvent(eventType);
        }
        private void SliceImage(object sender, RoutedEventArgs e)
        {
            ClosableTabItem cti = tabMain.SelectedItem as ClosableTabItem;
            if (cti == null)
            {
                DialogBox db = new DialogBox("Image series not selected.", "Please navigate to an Image Display tab and try again.",
                    "Images", DialogIcon.Error);
                db.ShowDialog();
                return;
            }

            DisplayTab it = cti.Content as DisplayTab;
            if (it == null)
            {
                DialogBox db = new DialogBox("Image series not selected.", "Please navigate to an Image Display tab and try again.",
                        "Images", DialogIcon.Error);
                db.ShowDialog();
                return;
            }

            if (it.ItemsControl.SelectedItems.Count == 0)
            {
                DialogBox db = new DialogBox("No images selected.", "Select two or more images to perform the 3D slice on and try again.",
                          "Images", DialogIcon.Error);
                db.ShowDialog();
                return;
            }
            else if (it.ItemsControl.SelectedItems.Count == 1)
            {
                DialogBox db = new DialogBox("Insufficient images selected.", "Select two or more images to perform the 3D slice on and try again.",
                             "Images", DialogIcon.Error);
                db.ShowDialog();
                return;
            }

            RibbonMenuItem rmi = (RibbonMenuItem)sender;
            if (sender == null) return;

            int parameter = Workspace.ImagingSlicePixel;
            ImageTabEvent eventType = ImageTabEvent.Empty;

            if (rmi == ribbonButtonXZSlice)
            {
                eventType = ImageTabEvent.SliceXZ;
            }
            else if (rmi == ribbonButtonYZSlice)
            {
                eventType = ImageTabEvent.SliceYZ;
            }

            it.CallEvent(eventType, parameter);
        }

        private void OnTextBoxKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            TextBox tb = sender as TextBox;
            if (tb != null)
            {
                tb.SelectAll();
            }
        }
        #endregion

        #region Render Tab
        private void LoadVolume(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "ImagingSIMS Volume Data (.vol)|*.vol";
            ofd.Multiselect = true;
            Nullable<bool> result = ofd.ShowDialog();
            if (result != true) return;

            foreach (string s in ofd.FileNames)
            {
                if (!File.Exists(s)) continue;
                Workspace.Volumes.Add(new Volume(s));
            }
        }
        private void SaveVolume(object sender, RoutedEventArgs e)
        {
            if (listViewVolumes.SelectedItems.Count == 0)
            {
                DialogBox db = new DialogBox("No volumes selected.", "No volumes have been selected to save. Select one or more and try again.",
                    "Volume", DialogIcon.Error);
                db.ShowDialog();
                return;
            }

            List<KeyValuePair<object, string>> notSaved = new List<KeyValuePair<object, string>>();

            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "ImagingSIMS Volume Data (.vol)|*.vol|Raw Volume Data (.raw)|*.raw";
            Nullable<bool> result = sfd.ShowDialog();
            if (result != true) return;

            if (listViewVolumes.SelectedItems.Count == 1)
            {
                try
                {
                    if (sfd.FilterIndex == 1)
                    {
                        ((Volume)listViewVolumes.SelectedItem).Save(sfd.FileName);
                    }
                    else
                    {
                        ((Volume)listViewVolumes.SelectedItem).SaveRawVolume(sfd.FileName);
                    }
                }
                catch (Exception ex)
                {
                    notSaved.Add(new KeyValuePair<object, string>(listViewVolumes.SelectedItem, ex.Message));
                }
            }
            else
            {
                int ct = 0;
                foreach (object obj in listViewVolumes.SelectedItems)
                {
                    string fileName = sfd.FileName.Insert(sfd.FileName.Length - 4, "_" + (++ct).ToString()); ;
                    Volume v = (Volume)obj;
                    if (v == null)
                    {
                        notSaved.Add(new KeyValuePair<object, string>(obj,
                            "Could not convert to volume type."));
                    }

                    try
                    {
                        if (sfd.FilterIndex == 1)
                        {
                            v.Save(fileName);
                        }
                        else
                        {
                            v.SaveRawVolume(fileName);
                        }

                    }
                    catch (Exception ex)
                    {
                        notSaved.Add(new KeyValuePair<object, string>(v, ex.Message));
                    }
                }
            }

            DialogBox dbResult;
            if (notSaved.Count > 0)
            {
                string list = "";
                foreach (KeyValuePair<object, string> kvp in notSaved)
                {
                    list += string.Format("{0}: {1}\n", kvp.Key.ToString(), kvp.Value);
                }
                list = list.Remove(list.Length - 2, 2);
                dbResult = new DialogBox("The following volumes could not be saved:", list, "Volume", DialogIcon.Warning);
                dbResult.ShowDialog();
            }
            else
            {
                dbResult = new DialogBox("Volume(s) saved successfully!", "", "Volume", DialogIcon.Ok);
                dbResult.ShowDialog();
            }
        }
        private void CreateVolume(object sender, RoutedEventArgs e)
        {
            VolumeTab vt = new VolumeTab();
            ClosableTabItem cti = ClosableTabItem.Create(vt, TabType.RenderObject, "New Volume");

            tabMain.Items.Add(cti);
            tabMain.SelectedItem = cti;
        }
        private void VolumeCreated(object sender, RoutedEventArgs e)
        {
            VolumeTab vt = (VolumeTab)e.Source;
            Workspace.Volumes.Add(vt.CreatedVolume);

            if (tabMain.Items.Contains(vt))
            {
                tabMain.Items.Remove(vt);
            }
        }

        private async void Render3D(object sender, RoutedEventArgs e)
        {
            int numberVolumes = listBoxRenderingVolumes.SelectedItems.Count;
            if (numberVolumes == 0)
            {
                DialogBox db = new DialogBox("No volumes selected.", "Select one or more volumes to render.",
                    "Rendering", DialogIcon.Error);
                db.ShowDialog();
                return;
            }

            List<Volume> volumes = new List<Volume>();
            float maxIntensity = 0;

            foreach (object obj in listBoxRenderingVolumes.SelectedItems)
            {
                Volume v = obj as Volume;
                if (v != null)
                {
                    volumes.Add(v);
                    if (v.Data.SingluarMaximum > maxIntensity) maxIntensity = v.Data.SingluarMaximum;
                }
            }

            if (checkBoxZCorrect.IsChecked == true)
            {
                if (Workspace.ImagingZCorrectThresh < 0)
                {
                    DialogBox.Show("Invalid Z-Correct threshold.",
                        "Enter a threshold greater than or equal to zero and try again.", "Z-Correct", DialogIcon.Error);
                    return;
                }
                try
                {
                    ProgressWindow pw = new ProgressWindow("Z-Correcting. Please wait...", "Z-Correct", true);
                    pw.Show();

                    volumes = await ZCorrection3D.CorrectAsync(volumes, Workspace.ImagingZCorrectThresh);

                    pw.Close();
                    pw = null;
                }
                catch (Exception ex)
                {
                    string message = ex.Message;
                    if (ex.InnerException != null) message += ex.InnerException.Message;

                    DialogBox.Show("Could not perform Z-Correction on the selected volumes.",
                        message, "Z-Correct", DialogIcon.Error);
                    return;
                }
            }

            List<RenderVolume> renderVolumes = new List<RenderVolume>();

            foreach (Volume volume in volumes)
            {
                //if (comboRenderingNorm.SelectedIndex == 1)
                //{
                //    renderVolumes.Add(new RenderVolume(volume.Data.ToFloatArray(true), volume.DataColor));
                //}
                //else
                //{
                //    renderVolumes.Add(new RenderVolume(volume.Data.ToFloatArray(maxIntensity), volume.DataColor));
                //}

                renderVolumes.Add(new RenderVolume(volume.Data.ToFloatArray(maxIntensity), volume.DataColor));
            }



            RenderWindow window = new RenderWindow();
            try
            {
                window.Show();

                await window.SetDataAsync(renderVolumes);
                window.BeginRendering();
            }
            catch(DllNotFoundException dnfEx)
            {
                Dialog.Show("The rendering engine could not be started because a necessary DLL was missing. Click the link below for more information.",
                    dnfEx.Message, "Rendering", DialogIcon.Error, "https://github.com/ImagingSIMS/ImagingSIMS/wiki/D3DCompiler_47.dll-Missing");

                if (window != null)
                {
                    window.Close();
                    window = null;
                }

                return;
            }
            catch(FileNotFoundException fnfEx)
            {
                Dialog.Show("The rendering engine could not be started because a necessary DLL was missing. Click the link below for more information.",
                    fnfEx.Message, "Rendering", DialogIcon.Error, "https://github.com/ImagingSIMS/ImagingSIMS/wiki/D3DCompiler_47.dll-Missing");

                if (window != null)
                {
                    window.Close();
                    window = null;
                }

                return;
            }
            catch(BadImageFormatException bimfEx)
            {
                Dialog.Show("The rendering engine could not be started because a necessary DLL was the wrong format. Click the link below for more information and ensure the DLLs with the correct bitness have been copied.",
                    bimfEx.Message, "Rendering", DialogIcon.Error, "https://github.com/ImagingSIMS/ImagingSIMS/wiki/D3DCompiler_47.dll-Missing");

                if (window != null)
                {
                    window.Close();
                    window = null;
                }

                return;
            }
            catch (Exception ex)
            {
                DialogBox db = new DialogBox("There was an error creating the 3D rendering.", ex.Message, "Direct3D", DialogIcon.Error);
                db.ShowDialog();

                if (window != null)
                {
                    window.Close();
                    window = null;
                }

                return;
            }
        }
        private async void Render3DIso(object sender, RoutedEventArgs e)
        {
            List<Volume> selectedVolumes = new List<Volume>();

            foreach (object obj in listBoxRenderingIsosurfaces.SelectedItems)
            {
                Volume v = obj as Volume;
                if (v != null)
                {
                    selectedVolumes.Add(v);
                }
            }

            if (selectedVolumes.Count == 0)
            {
                DialogBox.Show("No volumes selected.",
                    "Select one or more volumes to generate isosurfaces.", "Isosurface", DialogIcon.Error);
                return;
            }
           
            int width = 0;
            int height = 0;
            int depth = 0;

            if (!selectedVolumes.EnsureDimensions(out width, out height, out depth))
            {
                DialogBox.Show("Invalid volume dimensions.",
                        "The dimensions of one or more volumes does not match across all selected volumes.", "Isosurface", DialogIcon.Error);
                return;
            }

            if (Workspace.IsosurfaceDoSmooth)
            {
                if(Workspace.IsosurfaceSmoothWindowSize <= 0)
                {
                    DialogBox.Show("Invalid window size.", "Smoothing window size must be a positive integer.", "Isosurface", DialogIcon.Error);
                    return;
                }
            }

            pw = new ProgressWindow("Generating isosurfaces. Please wait...", "Isosurfacing", true);
            pw.Show();

            List<RenderIsosurface> isosurfaces = new List<RenderIsosurface>();
            try
            {               
                int ct = 0;

                foreach (var volume in selectedVolumes)
                {
                    Data3D d;
                    if (Workspace.IsosurfaceDoSmooth)
                    {
                        d = await volume.Data.SmoothAsync(Workspace.IsosurfaceSmoothWindowSize);
                    }
                    else d = volume.Data;

                    isosurfaces.Add(await RenderIsosurface.CreateSurfaceAsync(
                        d.ToFloatArray(), volume.IsoValue, volume.DataColor.ToSharpDXColor(), ct++));
                }
            }
            catch(Exception ex)
            {
                DialogBox.Show("The isosurfaces could not be generated.",
                    ex.Message, "Isosurfacing", DialogIcon.Error);
                return;
            }
            finally
            {
                pw.Close();
                pw = null;
            }

            RenderWindow window = new RenderWindow();
            try
            {
                window.Show();

                await window.SetDataAsync(isosurfaces);
                window.BeginRendering();
            }
            catch (DllNotFoundException dnfEx)
            {
                Dialog.Show("The rendering engine could not be started because a necessary DLL was missing. Click the link below for more information.",
                    dnfEx.Message, "Rendering", DialogIcon.Error, "https://github.com/ImagingSIMS/ImagingSIMS/wiki/D3DCompiler_47.dll-Missing");

                if (window != null)
                {
                    window.Close();
                    window = null;
                }

                return;
            }
            catch (FileNotFoundException fnfEx)
            {
                Dialog.Show("The rendering engine could not be started because a necessary DLL was missing. Click the link below for more information.",
                    fnfEx.Message, "Rendering", DialogIcon.Error, "https://github.com/ImagingSIMS/ImagingSIMS/wiki/D3DCompiler_47.dll-Missing");

                if (window != null)
                {
                    window.Close();
                    window = null;
                }

                return;
            }
            catch (BadImageFormatException bimfEx)
            {
                Dialog.Show("The rendering engine could not be started because a necessary DLL was the wrong format. Click the link below for more information and ensure the DLLs with the correct bitness have been copied.",
                    bimfEx.Message, "Rendering", DialogIcon.Error, "https://github.com/ImagingSIMS/ImagingSIMS/wiki/D3DCompiler_47.dll-Missing");

                if (window != null)
                {
                    window.Close();
                    window = null;
                }

                return;
            }
            catch (Exception ex)
            {
                DialogBox db = new DialogBox("There was an error creating the 3D rendering.", ex.Message, "Direct3D", DialogIcon.Error);
                db.ShowDialog();

                if (window != null)
                {
                    window.Close();
                    window = null;
                }

                return;
            }
        }
        private async void RenderHeightMap(object sender, RoutedEventArgs e)
        {
            ClosableTabItem cti = tabMain.SelectedItem as ClosableTabItem;
            if (cti == null) return;

            HeightMapTab hmt = cti.Content as HeightMapTab;
            if (hmt == null) return;

            if (hmt.HeightData == null)
            {
                DialogBox db = new DialogBox("No height data loaded.",
                    "Drag-Drop an image to serve as the height data for the depth render and try again.", "Depth Render", DialogIcon.Error);
                db.ShowDialog();
                return;
            }
            if (hmt.ColorData == null)
            {
                DialogBox db = new DialogBox("No color data loaded.",
                    "Drag-Drop an image to serve as the color data for the depth render and try again.", "Depth Render", DialogIcon.Error);
                db.ShowDialog();
                return;
            }

            HeightMap hm;
            try
            {
                hm = new HeightMap(hmt.HeightData, hmt.ColorData);
            }
            catch (ArgumentException ARex)
            {
                DialogBox db = new DialogBox("Could not create HeightMap.",
                    ARex.Message, "Depth Render", DialogIcon.Error);
                db.ShowDialog();
                return;
            }
            catch (Exception ex)
            {
                string message = ex.Message;
                if (ex.InnerException != null) message += "\n" + ex.InnerException.Message;

                DialogBox db = new DialogBox("Could not create HeightMap.",
                    message, "Depth Render", DialogIcon.Error);
                db.ShowDialog();
                return;
            }

            RenderWindow window = new RenderWindow();
            try
            {
                window.Show();

                await window.SetDataAsync(hm.CorrectedHeightData, hm.CorrectedColorData);
                window.BeginRendering();
            }
            catch (DllNotFoundException dnfEx)
            {
                Dialog.Show("The rendering engine could not be started because a necessary DLL was missing. Click the link below for more information.",
                    dnfEx.Message, "Rendering", DialogIcon.Error, "https://github.com/ImagingSIMS/ImagingSIMS/wiki/D3DCompiler_47.dll-Missing");

                if (window != null)
                {
                    window.Close();
                    window = null;
                }

                return;
            }
            catch (FileNotFoundException fnfEx)
            {
                Dialog.Show("The rendering engine could not be started because a necessary DLL was missing. Click the link below for more information.",
                    fnfEx.Message, "Rendering", DialogIcon.Error, "https://github.com/ImagingSIMS/ImagingSIMS/wiki/D3DCompiler_47.dll-Missing");

                if (window != null)
                {
                    window.Close();
                    window = null;
                }

                return;
            }
            catch (BadImageFormatException bimfEx)
            {
                Dialog.Show("The rendering engine could not be started because a necessary DLL was the wrong format. Click the link below for more information and ensure the DLLs with the correct bitness have been copied.",
                    bimfEx.Message, "Rendering", DialogIcon.Error, "https://github.com/ImagingSIMS/ImagingSIMS/wiki/D3DCompiler_47.dll-Missing");

                if (window != null)
                {
                    window.Close();
                    window = null;
                }

                return;
            }
            catch (Exception ex)
            {
                DialogBox db = new DialogBox("There was an error creating the 3D rendering.", ex.Message, "Direct3D", DialogIcon.Error);
                db.ShowDialog();

                if (window != null)
                {
                    window.Close();
                    window = null;
                }

                return;
            }
        }

        private void createDepthRender_Click(object sender, RoutedEventArgs e)
        {
            ClosableTabItem cti = ClosableTabItem.Create(new HeightMapTab(),
                TabType.HeightMap, "Depth Render");
            tabMain.Items.Add(cti);
            tabMain.SelectedItem = cti;
        }
        #endregion

        #region Spectra Tab
        private void ribbonButtonSpecCrop_Click(object sender, RoutedEventArgs e)
        {
            ClosableTabItem cti = ClosableTabItem.Create(new SpectrumCropTab(), TabType.SpectrumCrop, "Spectrum Crop");
            tabMain.Items.Add(cti);
            tabMain.SelectedItem = cti;
        }

        private void SpecRangeUpdated(object sender, RangeUpdatedRoutedEventArgs e)
        {
            SpectrumTab st = sender as SpectrumTab;
            if (st == null) return;

            //specViewBack.IsEnabled = st.CanHistoryBack;
            //specViewNext.IsEnabled = st.CanHistoryForward;
        }
        private void SpecSelectionRangeUpdated(object sender, RangeUpdatedRoutedEventArgs e)
        {
            if (radioSpecSingleRange.IsChecked == true)
            {
                Workspace.SpectraMassStart = e.MassStart;
                Workspace.SpectraMassEnd = e.MassEnd;

                double avg = MathEx.Average(Workspace.SpectraMassStart, Workspace.SpectraMassEnd);
                specTableName.Text = avg.ToString("0.0");
            }
            else if (radioSpecCustomRange.IsChecked == true)
            {
                string currentRange = Workspace.SpectraCustomRange;

                // Text clears does not clear ranges
                try
                {
                    List<MassRange> ranges = MassRange.ParseString(currentRange);
                    ranges.Add(new MassRange(e.MassStart, e.MassEnd));
                    ranges.Sort();
                    currentRange = MassRange.CreateString(ranges);
                }
                finally
                {
                    Workspace.SpectraCustomRange = currentRange;
                }
            }
        }

        private void specCreateTables_Click(object sender, RoutedEventArgs e)
        {
            SpectrumTab st = ((ClosableTabItem)tabMain.SelectedItem).Content as SpectrumTab;
            if (st == null)
            {
                DialogBox db = new DialogBox("No spectrum selected.", "Navigate to a loaded spectrum and try again.",
                    "Spectrum", DialogIcon.Error);
                db.ShowDialog();
                return;
            }

            // Args layout:
            // [0]: (Spectrum)          Spectrum to process
            // [1]: (string)            Base name
            // [2]: (bool)              Omit data numbering
            // [2]: (MassRangePair[])   Mass ranges   

            MassRange[] massRanges = new MassRange[1];

            if (radioSpecSingleRange.IsChecked == true)
            {
                massRanges[0] = new MassRange()
                {
                    StartMass = Workspace.SpectraMassStart,
                    EndMass = Workspace.SpectraMassEnd
                };

            }
            else if (radioSpecCustomRange.IsChecked == true)
            {
                try
                {
                    massRanges = MassRange.ParseString(Workspace.SpectraCustomRange).ToArray();
                }
                catch (ArgumentException ARex)
                {
                    string message = ARex.Message;
                    if (ARex.InnerException != null)
                    {
                        message += ": " + ARex.InnerException.Message;
                    }

                    DialogBox.Show("Could not parse the specified mass ranges.", message, "Spectra", DialogIcon.Error);
                    return;
                }
                catch (Exception ex)
                {
                    string message = ex.Message;
                    if (ex.InnerException != null)
                    {
                        message += ": " + ex.InnerException.Message;
                    }

                    DialogBox.Show("Could not parse the specified mass ranges.", message, "Spectra", DialogIcon.Error);
                    return;
                }
            }
            else
            {
                DialogBox.Show("Value not selected.",
                    "Could not determine the desired type of range.", "Mass Range", DialogIcon.Error);
                return;
            }
            // Validate common parameters
            try
            {
                if (st.Spectrum == null)
                {
                    throw new ArgumentException("Null spectrum.");
                }
                if (specTableName.Text == null || specTableName.Text == "")
                {
                    throw new ArgumentException("Specify a base name for the generated tables and try again.");
                }
            }
            catch (ArgumentException ARex)
            {
                DialogBox.Show("Invalid parameter.", ARex.Message,
                    "Tables", DialogIcon.Error);
                return;
            }
            // Validate all mass ranges
            try
            {
                foreach (MassRange range in massRanges)
                {
                    double start = range.StartMass;
                    double end = range.EndMass;

                    if (end <= start)
                    {
                        throw new ArgumentException($"Ensure the end mass {end} is not less than or equal to the start mass {start}.");
                    }
                    if (start < st.Spectrum.StartMass || start >= st.Spectrum.EndMass)
                    {
                        throw new ArgumentException($"Start mass {start} does not fall within the spectrum's mass range.");
                    }
                    if (end > st.Spectrum.EndMass || end <= st.Spectrum.StartMass)
                    {
                        throw new ArgumentException($"End mass {end} does not fall within the spectrum's mass range.");
                    }
                }
            }
            catch (ArgumentException ARex)
            {
                DialogBox.Show("Invalid parameter.", ARex.Message,
                    "Tables", DialogIcon.Error);
                return;
            }

            // Set up background worker
            BackgroundWorker bw = new BackgroundWorker();
            bw.WorkerReportsProgress = true;
            bw.RunWorkerCompleted += SpecTables_RunWorkerCompleted;
            bw.ProgressChanged += bw_ProgressChanged;
            bw.DoWork += SpecTables_DoWork;

            pw = new ProgressWindow("Generating tables from spectrum. Please wait.", "Tables");
            pw.Show();

            // Run with args
            object[] args = new object[4]
            {
                st.Spectrum, specTableName.Text, Workspace.OmitDataNumbering, massRanges
            };
            bw.RunWorkerAsync(args);
        }
        private void specCreateDepthProfile_Click(object sender, RoutedEventArgs e)
        {
            SpectrumTab st = ((ClosableTabItem)tabMain.SelectedItem).Content as SpectrumTab;
            if (st == null)
            {
                DialogBox db = new DialogBox("No spectrum selected.", "Navigate to a loaded spectrum and try again.",
                    "Spectrum", DialogIcon.Error);
                db.ShowDialog();
                return;
            }

            // Args layout:
            // [0]: (Spectrum)          Spectrum to process
            // [1]: (string)            Output base name
            // [2]: (MassRangePair[])   Mass ranges        

            MassRange[] massRanges = new MassRange[1];

            if (radioSpecSingleRange.IsChecked == true)
            {
                massRanges[0] = new MassRange()
                {
                    StartMass = Workspace.SpectraMassStart,
                    EndMass = Workspace.SpectraMassEnd
                };

            }
            else if (radioSpecCustomRange.IsChecked == true)
            {
                try
                {
                    massRanges = MassRange.ParseString(Workspace.SpectraCustomRange).ToArray();
                }
                catch (ArgumentException ARex)
                {
                    string message = ARex.Message;
                    if (ARex.InnerException != null)
                    {
                        message += ": " + ARex.InnerException.Message;
                    }

                    DialogBox.Show("Could not parse the specified mass ranges.", message, "Spectra", DialogIcon.Error);
                    return;
                }
            }
            else
            {
                DialogBox.Show("Value not selected.",
                    "Could not determine the desired type of range.", "Mass Range", DialogIcon.Error);
                return;
            }
            // Validate common parameters
            try
            {
                if (st.Spectrum == null)
                {
                    throw new ArgumentException("Null spectrum.");
                }
            }
            catch (ArgumentException ARex)
            {
                DialogBox.Show("Invalid parameter.", ARex.Message,
                    "Depth Profile", DialogIcon.Error);
                return;
            }

            if (st.Spectrum.SizeZ == 1)
            {
                if (DialogBox.Show("No depth to the spectrum.",
                   "The spectrum only has one layer, which doesn't really make a depth profile. Click OK to continue anyway or Cancel to return.",
                   "Depth Profile", DialogIcon.Help, true) != true)
                    return;
            }
            // Validate all mass ranges
            try
            {
                foreach (MassRange range in massRanges)
                {
                    double start = range.StartMass;
                    double end = range.EndMass;

                    if (end <= start)
                    {
                        throw new ArgumentException($"Ensure the end mass {end} is not less than or equal to the start mass {start}.");
                    }
                    if (start < st.Spectrum.StartMass || start >= st.Spectrum.EndMass)
                    {
                        throw new ArgumentException($"Start mass {start} does not fall within the spectrum's mass range.");
                    }
                    if (end > st.Spectrum.EndMass || end <= st.Spectrum.StartMass)
                    {
                        throw new ArgumentException($"End mass {end} does not fall within the spectrum's mass range.");
                    }
                }
            }
            catch (ArgumentException ARex)
            {
                DialogBox.Show("Invalid parameter.", ARex.Message,
                    "Depth Profile", DialogIcon.Error);
                return;
            }

            // Get output file name
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "Depth profile (.txt)|*.txt";
            sfd.FileName = specTableName.Text.Replace('.', '_');
            if (sfd.ShowDialog() != true) return;

            // Set up background worker
            BackgroundWorker bw = new BackgroundWorker();
            bw.WorkerSupportsCancellation = false;
            bw.WorkerReportsProgress = true;
            bw.RunWorkerCompleted += SpecDP_RunWorkerCompleted;
            bw.ProgressChanged += bw_ProgressChanged;
            bw.DoWork += SpecDP_DoWork;

            pw = new ProgressWindow("Generating depth profile...", "Depth Profile");
            pw.Show();

            // Run with args
            object[] args = new object[3]
            {
                st.Spectrum, sfd.FileName, massRanges
            };
            bw.RunWorkerAsync(args);
        }

        void SpecTables_DoWork(object sender, DoWorkEventArgs e)
        {
            // Args layout:
            // [0]: (Spectrum)          Spectrum to process
            // [1]: (string)            Base name
            // [2]: (bool)              Omit data numbering
            // [2]: (MassRangePair[])   Mass ranges  
            object[] args = (object[])e.Argument;

            Spectrum spectrum = (Spectrum)args[0];
            string baseName = (string)args[1];
            bool omitNumbering = (bool)args[2];
            MassRange[] ranges = (MassRange[])args[3];

            List<Data2D> tables = new List<Data2D>();

            int numberRanges = ranges.Length;

            for (int i = 0; i < ranges.Length; i++)
            {
                MassRange range = ranges[i];
                double startMass = range.StartMass;
                double endMass = range.EndMass;

                // Use Dispatcher since this function is being run by a background thread
                Dispatcher.Invoke(() =>
                    pw.UpdateMessage(string.Format("Generating tables for range {0} of {1}: {2} - {3}. Please wait...",
                        (i + 1).ToString(), numberRanges, startMass, endMass))
                );

                tables.AddRange(spectrum.FromMassRange(new MassRange(range.StartMass, range.EndMass),
                    baseName, omitNumbering, sender as BackgroundWorker));
            }

            e.Result = tables;
        }
        void SpecTables_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            try
            {
                List<Data2D> tables = (List<Data2D>)e.Result;
                if (tables == null) throw new ArgumentException("The generation process could not be completed.");

                foreach (Data2D d in tables)
                {
                    Workspace.Data.Add(d);
                }

                pw.ProgressFinished("Table generation complete!");
            }
            catch (ArgumentException ARex)
            {
                pw.Close();
                DialogBox db = new DialogBox(ARex.Message, "Please try again.", "Tables", DialogIcon.Error);
                db.ShowDialog();
            }
            finally
            {
                BackgroundWorker bw = sender as BackgroundWorker;

                bw.RunWorkerCompleted -= SpecTables_RunWorkerCompleted;
                bw.ProgressChanged -= bw_ProgressChanged;
                bw.DoWork -= SpecTables_DoWork;
                bw.Dispose();
            }
        }

        void SpecDP_DoWork(object sender, DoWorkEventArgs e)
        {
            // Args layout:
            // [0]: (Spectrum)          Spectrum to process
            // [1]: (string)            Output base name
            // [2]: (MassRangePair[])   Mass ranges    
            object[] args = (object[])e.Argument;

            Spectrum spectrum = (Spectrum)args[0];
            string baseSavePath = (string)args[1];
            MassRange[] ranges = (MassRange[])args[2];

            int numberRanges = ranges.Length;

            List<string> savedPaths = new List<string>();
            for (int i = 0; i < ranges.Length; i++)
            {
                MassRange range = ranges[i];
                double startMass = range.StartMass;
                double endMass = range.EndMass;

                Dispatcher.Invoke(() =>
                pw.UpdateMessage(string.Format("Generating depth profile for range {0} of {1}: {2} - {3}. Please wait...",
                        (i + 1).ToString(), numberRanges, startMass, endMass))
                );

                double[] depthProfile = spectrum.CreateDepthProfile(new MassRange(startMass, endMass), sender as BackgroundWorker);

                string rangeString = startMass.ToString("0.000") + "-" + endMass.ToString("0.000");
                string savePath = baseSavePath.Insert(baseSavePath.Length - 4, rangeString);

                using (Stream stream = File.OpenWrite(savePath))
                {
                    StreamWriter sw = new StreamWriter(stream);

                    sw.WriteLine("File name:     " + Path.GetFileName(savePath));
                    sw.WriteLine("Spectrum name: " + spectrum.Name);
                    sw.WriteLine("Start mass:    " + startMass.ToString("0.000"));
                    sw.WriteLine("End mass:      " + endMass.ToString("0.000"));

                    if (spectrum.SpectrumType == SpectrumType.BioToF)
                    {
                        int startBin = ((BioToFSpectrum)spectrum).MassToTime(Workspace.SpectraMassStart);
                        int endBin = ((BioToFSpectrum)spectrum).MassToTime(Workspace.SpectraMassEnd);

                        sw.WriteLine("Start bin:     " + startBin.ToString());
                        sw.WriteLine("End bin:       " + endBin.ToString());
                    }

                    //Write blank line to separate header info from data
                    sw.WriteLine();

                    for (int z = 0; z < depthProfile.Length; z++)
                    {
                        sw.WriteLine(depthProfile[z]);
                    }

                    sw.Close();
                }

                savedPaths.Add(savePath);
            }

            e.Result = savedPaths;
        }
        void SpecDP_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            BackgroundWorker bw_dp = sender as BackgroundWorker;
            if (e.Result == null)
            {
                DialogBox.Show("The depth profile failed to save.",
                    "Verify the integrity of the spectrum and try again.", "Depth Profile", DialogIcon.Error);
                return;
            }

            List<string> savedPaths = (List<string>)e.Result;

            if (pw != null)
            {
                pw.Close();
                pw = null;
            }

            bw_dp.RunWorkerCompleted -= SpecDP_RunWorkerCompleted;
            bw_dp.ProgressChanged -= bw_ProgressChanged;
            bw_dp.DoWork -= SpecDP_DoWork;
            bw_dp.Dispose();

            Process p = new Process();
            p.StartInfo = new ProcessStartInfo(savedPaths[0]);
            p.Start();

            string savedPathsString = String.Empty;
            foreach (string s in savedPaths)
            {
                savedPathsString += s + "\n";
            }
            savedPathsString.Remove(savedPathsString.Length - 2);

            DialogBox.Show("Depth profile saved successfully!", savedPathsString, "Depth Profile", DialogIcon.Ok);
        }

        private async void specShowPreview_Click(object sender, RoutedEventArgs e)
        {
            SpectrumTab st = ((ClosableTabItem)tabMain.SelectedItem).Content as SpectrumTab;
            st.PreviewVisibility = Visibility.Visible;
            await st.CreatePreview();
        }
        private void specHidePreview_Click(object sender, RoutedEventArgs e)
        {
            SpectrumTab st = ((ClosableTabItem)tabMain.SelectedItem).Content as SpectrumTab;
            st.PreviewVisibility = Visibility.Collapsed;
        }

        private void specExport_Click(object sender, RoutedEventArgs e)
        {
            int binSize;

            if (string.IsNullOrEmpty(ribbonTextBoxSpecExportBinSize.Text))
            {
                binSize = 1;
            }
            else if (!int.TryParse(ribbonTextBoxSpecExportBinSize.Text, out binSize))
            {
                DialogBox.Show("Invalid bin size.",
                    "Enter an integer greater than zero and try again.", "Export", DialogIcon.Error);
                return;
            }

            if (binSize <= 0)
            {
                DialogBox.Show("Invalid bin size.",
                   "Enter an integer greater than zero and try again.", "Export", DialogIcon.Error);
                return;
            }

            ClosableTabItem cti = tabMain.SelectedItem as ClosableTabItem;
            if (cti == null) return;

            SpectrumTab st = cti.Content as SpectrumTab;
            if (st == null) return;

            Spectrum s = st.Spectrum;

            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "Text Files (.txt)|*.txt";
            if (sfd.ShowDialog() != true) return;

            s.SaveText(sfd.FileName, binSize);
        }

        private void SpecDeadTimeCorrect_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            var canCorrect = GetAvailableSpectra().Where(s => s.SpectrumType == SpectrumType.CamecaNanoSIMS);

            e.CanExecute = canCorrect.Count() > 0;
        }
        private void SpecDeadTimeCorrect_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            bool isCorrect = true;
            try
            {
                string param = (string)e.Parameter;
                if (param.ToLower() == "revert") isCorrect = false;                
            }
            catch (Exception)
            {
                isCorrect = true;
            }


            var toProcess = GetSelectedSpectra()
                .Where(s => s.SpectrumType == SpectrumType.CamecaNanoSIMS)
                .Select(s => s as CamecaNanoSIMSSpectrum);

            if(toProcess.Count() == 0)
            {
                DialogBox.Show("No spectra selected.", "Only Cameca NanoSIMS spectra can be corrected.", "Dead Time Correction", DialogIcon.Warning);
                return;
            }

            Dictionary<CamecaNanoSIMSSpectrum, string> notProcessed = new Dictionary<CamecaNanoSIMSSpectrum, string>();
            foreach (var spec in toProcess)
            {
                try
                {
                    if (isCorrect)
                    {
                        if (spec.IsDeadTimeCorrected)
                        {
                            notProcessed.Add(spec, "Already dead time corrected");
                        }
                        else spec.DeadTimeCorrect();
                    }
                    else
                    {
                        if (!spec.IsDeadTimeCorrected)
                        {
                            notProcessed.Add(spec, "Not dead time corrected");
                        }
                        else spec.RemoveDeadTimeCorrection();
                    }
                }
                catch(Exception ex)
                {
                    notProcessed.Add(spec, ex.Message);
                }
            }

            if (notProcessed.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                foreach (var item in notProcessed)
                {
                    sb.Append($"{item.Key.Name}: {item.Value}\n");
                }

                string list = sb.Remove(sb.Length - 1, 1).ToString();

                DialogBox.Show("The following spectra could not be processed. Spectra not listed below were processed succesfully", 
                    list, "Dead Time Correction", DialogIcon.Error);
            }
            else
            {
                string message = string.Empty;
                if (isCorrect)
                {
                    message = "Selected spectra were dead time corrected successfully.";
                }
                else message = "Dead time correction revered successfully for the selected spectra.";

                DialogBox.Show(message, "", "Dead Time Correction", DialogIcon.Ok);
            }
        }
        #endregion

        #region Images Tab
        private void RibbonImagesEvent(object sender, RoutedEventArgs e)
        {
            ClosableTabItem cti = tabMain.SelectedItem as ClosableTabItem;
            if (cti == null)
            {
                DialogBox db = new DialogBox("Image series not selected.", "Please navigate to an Image Display tab and try again.",
                    "Images", DialogIcon.Error);
                db.ShowDialog();
                return;
            }

            ImageTabEvent eventType = ImageTabEvent.Empty;
            if (e.Source == ribbonImagesCopy)
            {
                eventType = ImageTabEvent.Copy;
            }
            else if (e.Source == ribbonImagesRGB)
            {
                eventType = ImageTabEvent.ToRGB;
            }
            else if (e.Source == ribbonImagesRC)
            {
                eventType = ImageTabEvent.RotateClock;
            }
            else if (e.Source == ribbonImagesRCC)
            {
                eventType = ImageTabEvent.RotateCounter;
            }
            else if (e.Source == ribbonImagesFH)
            {
                eventType = ImageTabEvent.FlipHorizontal;
            }
            else if (e.Source == ribbonImagesFV)
            {
                eventType = ImageTabEvent.FlipVertical;
            }
            else if (e.Source == ribbonImagesResize)
            {
                eventType = ImageTabEvent.Resize;
            }
            TabType t = cti.TabType;

            switch (t)
            {
                case TabType.Display:
                    DisplayTab it = (DisplayTab)cti.Content;
                    if (it != null) it.CallEvent(eventType);
                    break;
                case TabType.SEM:
                    SEMTab st = (SEMTab)cti.Content;
                    if (st != null) st.CallEvent(eventType);
                    break;
                case TabType.Fusion:
                    FusionTab ft = (FusionTab)cti.Content;
                    if (ft != null) ft.CallEvent(eventType);
                    break;
                default:
                    DialogBox db = new DialogBox("Image series not selected.", "Please navigate to an Image Display tab and try again.",
                       "Images", DialogIcon.Error);
                    db.ShowDialog();
                    return;
            }
        }
        private void ResetImage(object sender, RoutedEventArgs e)
        {
            ClosableTabItem cti = tabMain.SelectedItem as ClosableTabItem;
            if (cti == null)
            {
                DialogBox db = new DialogBox("Image series not selected.", "Please navigate to an SEM Image Display tab and try again.",
                    "Images", DialogIcon.Error);
                db.ShowDialog();
                return;
            }
            SEMTab st = cti.Content as SEMTab;
            if (st == null)
            {
                DialogBox db = new DialogBox("Image series not selected.", "Please navigate to an SEM Image Display tab and try again.",
                        "Images", DialogIcon.Error);
                db.ShowDialog();
                return;
            }

            st.ResetImage();
        }
        private void RibbonCreateSeries(object sender, RoutedEventArgs e)
        {
            ClosableTabItem cti = tabMain.SelectedItem as ClosableTabItem;
            if (cti == null)
            {
                DialogBox db = new DialogBox("Image series not selected.", "Please navigate to an Image Display tab and try again.",
                    "Images", DialogIcon.Error);
                db.ShowDialog();
                return;
            }
            DisplayTab dt = cti.Content as DisplayTab;
            if (dt == null)
            {
                DialogBox db = new DialogBox("Image series not selected.", "Please navigate to an Image Display tab and try again.",
                                    "Images", DialogIcon.Error);
                db.ShowDialog();
                return;
            }

            DisplaySeries series = dt.CreateImageSeries();
            if (series == null) return;

            Workspace.ImageSeries.Add(series);
        }
        private async void RibbonImagesToData(object sender, RoutedEventArgs e)
        {
            ClosableTabItem cti = tabMain.SelectedItem as ClosableTabItem;
            if (cti == null)
            {
                Dialog.Show("Image series not selected.", "Please navigate to an Image Display tab and try again.",
                    "Images", DialogIcon.Error);
                return;
            }
            DisplayTab dt = cti.Content as DisplayTab;
            if(dt== null)
            {
                Dialog.Show("Image series not selected.", "Please navigate to an Image Display tab and try again.",
                    "Images", DialogIcon.Error);
                return;
            }

            DisplaySeries series = dt.CurrentSelectedSeries;
            if(series == null || series.Images.Count == 0)
            {
                Dialog.Show("No images selected.", "Select one or more images to convert and try again.",
                    "Convert", DialogIcon.Error);
                return;
            }

            BitmapSource[] toConvert = new BitmapSource[series.NumberImages];
            string[] titles = new string[series.NumberImages];
            for (int i = 0; i < series.NumberImages; i++)
            {
                toConvert[i] = series.Images[i].Source as BitmapSource;
                toConvert[i].Freeze();
                titles[i] = series.Images[i].Title;
            }

            for (int i = 0; i < toConvert.Length; i++)
            {
                Data2D converted = await ImageGenerator.Instance.ConvertToData2DAsync(toConvert[i]);
                converted.DataName = titles[i];
                Workspace.Data.Add(converted);
            }
        }
        private void openClusterTab_Click(object sender, RoutedEventArgs e)
        {
            ClosableTabItem cti = ClosableTabItem.Create(new ClusterTab(), TabType.Cluster, "Clusters");
            tabMain.Items.Add(cti);
            tabMain.SelectedItem = cti;
        }
        #endregion

        #region Fusion Tab
        private void OpenFusionTab(object sender, RoutedEventArgs e)
        {
            var button = sender as RibbonButton;
            if (sender == null) return;

            if(button == fusionNew)
            {
                ClosableTabItem cti = ClosableTabItem.Create(new FusionTab(), TabType.Fusion);
                tabMain.Items.Add(cti);
                tabMain.SelectedItem = cti;
            }
            else if(button == fusionPointNew)
            {
                var cti = ClosableTabItem.Create(new FusionPointTab(), TabType.FusionPoint, "Fusion");
                AddTabItemAndNavigate(cti);
            }
        }
        private void LoadFusionHigh(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Title = "Open High Res Image";
            ofd.Filter = "Image Files (.bmp, .jpg, .jpeg, .png)|*.bmp;*.jpg;*.jpeg;*.png;*.tif;*.tiff|" +
                            "Bitmap Images (.bmp)|*.bmp|JPEG Images (.jpg, .jpeg)|*.jpg;*.jpeg|PNG Images (.png)|*.png|Tiff Images (.tif, .tiff)|*.tif;*.tiff";
            ofd.Multiselect = false;
            if (ofd.ShowDialog() != true) return;

            BitmapImage src = new BitmapImage();
            src.BeginInit();
            src.UriSource = new Uri(ofd.FileName, UriKind.Absolute);
            src.CacheOption = BitmapCacheOption.OnLoad;
            src.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            src.EndInit();

            FusionTab ft = null;
            FusionPointTab frt = null;
            ClosableTabItem cti = tabMain.SelectedItem as ClosableTabItem;
            if (cti == null) goto NewTab;
            else
            {
                ft = cti.Content as FusionTab;
                if (ft != null) goto UseTab;
                foreach (TabItem ti in tabMain.Items)
                {
                    cti = (ClosableTabItem)ti;
                    if (cti == null) continue;

                    ft = cti.Content as FusionTab;
                    if (ft != null) goto UseTab;

                    frt = cti.Content as FusionPointTab;
                    if (frt != null) goto UseTab;
                }
            }

            NewTab:
            {
                frt = new FusionPointTab();
                cti = ClosableTabItem.Create(frt, TabType.FusionPoint);
                tabMain.Items.Add(cti);
                goto UseTab;
            }
            UseTab:
            {
                if(ft != null)
                {
                    ft.SetHighRes(src);
                }
                else if(frt != null)
                {
                    frt.SetImage(src, true);
                }

                tabMain.SelectedItem = cti;
            }
        }
        private void LoadFusionLow(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Title = "Open Low Res Image";
            ofd.Filter = "Image Files (.bmp, .jpg, .jpeg, .png)|*.bmp;*.jpg;*.jpeg;*.png;*.tif;*.tiff|" +
                            "Bitmap Images (.bmp)|*.bmp|JPEG Images (.jpg, .jpeg)|*.jpg;*.jpeg|PNG Images (.png)|*.png|Tiff Images (.tif, .tiff)|*.tif;*.tiff";
            ofd.Multiselect = false;
            if (ofd.ShowDialog() != true) return;

            BitmapImage src = new BitmapImage();
            src.BeginInit();
            src.UriSource = new Uri(ofd.FileName, UriKind.Absolute);
            src.CacheOption = BitmapCacheOption.OnLoad;
            src.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            src.EndInit();

            FusionTab ft = null;
            FusionPointTab frt = null;
            ClosableTabItem cti = tabMain.SelectedItem as ClosableTabItem;
            if (cti == null) goto NewTab;
            else
            {
                ft = cti.Content as FusionTab;
                if (ft != null) goto UseTab;
                foreach (TabItem ti in tabMain.Items)
                {
                    cti = (ClosableTabItem)ti;
                    if (cti == null) continue;

                    ft = cti.Content as FusionTab;
                    if (ft != null) goto UseTab;

                    frt = cti.Content as FusionPointTab;
                    if (frt != null) goto UseTab;
                }
            }

            NewTab:
            {
                ft = new FusionTab();
                cti = ClosableTabItem.Create(ft, TabType.Fusion);
                tabMain.Items.Add(cti);
                goto UseTab;
            }
            UseTab:
            {
                if (ft != null)
                {
                    ft.SetLowRes(src);
                }
                else if(frt != null)
                {
                    frt.SetImage(src, false);
                }

                tabMain.SelectedItem = cti;
            }
        }

        private void ribbonFusionSave_Click(object sender, RoutedEventArgs e)
        {
            ClosableTabItem cti = tabMain.SelectedItem as ClosableTabItem;
            if (cti == null)
            {
                DialogBox.Show("No document available to save.",
                    "Navigate to or open a new fusion tab and try again.", "Save", DialogIcon.Error);
                return;
            }

            FusionSaveParameter parameter = FusionSaveParameter.None;

            if (sender == ribbonFusionSaveFused)
            {
                parameter = FusionSaveParameter.Fused;
            }
            else if (sender == ribbonFusionSaveHigh)
            {
                parameter = FusionSaveParameter.HighRes;
            }
            else if (sender == ribbonFusionSaveLow)
            {
                parameter = FusionSaveParameter.LowRes;
            }

            if (parameter == FusionSaveParameter.None)
            {
                DialogBox.Show("Could not perform the selected operation.",
                    "For some reason, the save parameter could not be determined.", "Save", DialogIcon.Error);
                return;
            }

            FusionTab ft = cti.Content as FusionTab;
            if (ft != null)
            {
                ft.CallSave(parameter);
                return;
            }

            FusionPointTab frt = cti.Content as FusionPointTab;
            if (frt != null)
            {
                frt.SaveImage(parameter);
                return;
            }

            DialogBox.Show("No fusion document available to save.",
                "Navigate to or open a new fusion tab and try again.", "Save", DialogIcon.Error);
            return;
            
        }
        private void ribbonFusionSaveSeries_Click(object sender, RoutedEventArgs e)
        {
            ClosableTabItem cti = tabMain.SelectedItem as ClosableTabItem;
            if (cti == null)
            {
                DialogBox.Show("No document available to save.",
                    "Navigate to or open a new fusion tab and try again.", "Save", DialogIcon.Error);
                return;
            }

            FusionTab ft = cti.Content as FusionTab;
            if (ft != null)
            {
                ft.CallSave(FusionSaveParameter.Series);
                return;
            }

            FusionPointTab frt = cti.Content as FusionPointTab;
            if (frt != null)
            {
                frt.SaveSeries();
                return;
            }

            DialogBox.Show("No fusion document available to save.",
                "Navigate to or open a new fusion tab and try again.", "Save", DialogIcon.Error);
        }

        private void ribbonRegistrationOverlay_Click(object sender, RoutedEventArgs e)
        {
            ClosableTabItem cti = tabMain.SelectedItem as ClosableTabItem;
            if (cti == null)
            {
                DialogBox.Show("Not a registration enabled document.", "Navigate to a registration enabled tab and try again.",
                        "Registration", DialogIcon.Error);
            }

            if (cti.TabType == TabType.Fusion)
            {
                FusionTab ft = cti.Content as FusionTab;
                if (ft == null)
                {
                    DialogBox.Show("Not a valid fusion document.", "Navigate to a fusion tab and try again.",
                        "Registration", DialogIcon.Error);
                    return;
                }
                ft.OpenOverlay();
            }
            else if (cti.TabType == TabType.DataRegistration)
            {
                DataRegistrationTab dt = cti.Content as DataRegistrationTab;
                if(dt == null)
                {
                    DialogBox.Show("Not a valid data registration document.", "Navigate to a data registration tab and try again.",
                            "Registration", DialogIcon.Error);
                }
                dt.OpenOverlay();
            }
            else if (cti.TabType == TabType.FusionPoint)
            {
                FusionPointTab ft = cti.Content as FusionPointTab;
                if (ft == null)
                {
                    DialogBox.Show("Not a valid fusion document.", "Navigate to a fusion tab and try again.",
                        "Registration", DialogIcon.Error);
                    return;
                }
                ft.OpenOverlay();
            }

            else
            {
                DialogBox.Show("Not a registration enabled document.", "Navigate to a registration enabled tab and try again.",
                        "Registration", DialogIcon.Error);
                return;
            }

        }
        private void ribbonFusionViewFolder_Click(object sender, RoutedEventArgs e)
        {
            Process p = new Process();
            p.StartInfo = new ProcessStartInfo("explorer.exe", ImageRegistration.Registration.TransferFolderPath);
            p.Start();
        }
        private void ribbonFusionViewResults_Click(object sender, RoutedEventArgs e)
        {
            Process p = new Process();
            p.StartInfo = new ProcessStartInfo("explorer.exe", @"Plugins\RegistrationOutput.csv");
            p.Start();
        }
        #endregion

        #region Data Sidebar
        List<MenuItem> _colorScaleMenuItems;
        public List<MenuItem> ColorScaleMenuItems
        {
            get { return _colorScaleMenuItems; }
            set { _colorScaleMenuItems = value; }
        }

        private void CMWorkspaceRename_Click(object sender, RoutedEventArgs e)
        {
            string currentTitle = Workspace.WorkspaceName;
            TextEntryDialog te = new TextEntryDialog("Enter a new workspace name:", currentTitle);
            if (te.ShowDialog() != true) return;

            if (String.IsNullOrEmpty(te.EnteredText))
            {
                DialogBox.Show("Could not update the title of the selected object.",
                        "No text entered.", "Title", DialogIcon.Error);
                return;
            }
            if (char.IsWhiteSpace(te.EnteredText, 0))
            {
                DialogBox.Show("Could not update the title of the selected object.",
                    "First character is a space.", "Title", DialogIcon.Error);
                return;
            }

            Workspace.WorkspaceName = te.EnteredText;
        }
        private void CMWorkspaceClose_Click(object sender, RoutedEventArgs e)
        {
            if (!Workspace.HasContents) return;

            DialogBox db = new DialogBox("Are you sure you want to clear the current workspace?",
                "Click OK to delete all data or Cancel to return.",
                "Workspace", DialogIcon.Error, true);
            if (db.ShowDialog() == true)
            {
                Workspace = new Workspace();
                Workspace.InitializeRegistry();
            }
        }

        private void CMDataDelete(object sender, RoutedEventArgs e)
        {
            if (listViewData.SelectedItems.Count == 0) return;
            string msg = "";
            if (listViewData.SelectedItems.Count == 1) msg = "Delete 1 table?";
            else
            {
                msg = string.Format("Delete {0} tables?", listViewData.SelectedItems.Count);
            }
            DialogBox db = new DialogBox(msg,
                "Click OK to confirm or Cancel to return.", "Confirm", DialogIcon.Help, true);
            Nullable<bool> result = db.ShowDialog();
            if (result != true) return;

            Data2D[] toRemove = new Data2D[listViewData.SelectedItems.Count];
            for (int i = 0; i < listViewData.SelectedItems.Count; i++)
            {
                toRemove[i] = (Data2D)listViewData.SelectedItems[i];
            }
            List<KeyValuePair<Data2D, string>> notRemoved = new List<KeyValuePair<Data2D, string>>();
            for (int i = 0; i < toRemove.Length; i++)
            {
                try
                {
                    if (!Workspace.Data.Contains(toRemove[i])) throw new ArgumentException("Selected table is not present in the collection.");
                    Workspace.Data.Remove(toRemove[i]);
                }
                catch (ArgumentException ARex)
                {
                    notRemoved.Add(new KeyValuePair<Data2D, string>(toRemove[i], ARex.Message));
                }
                catch (Exception ex)
                {
                    notRemoved.Add(new KeyValuePair<Data2D, string>(toRemove[i], ex.Message));
                }
            }

            if (notRemoved.Count > 0)
            {
                string list = "";
                foreach (KeyValuePair<Data2D, string> kvp in notRemoved)
                {
                    list += string.Format("{0}: {1}\n", kvp.Key.ToString(), kvp.Value);
                }
                list.Remove(list.Length - 2, 2);

                DialogBox db1 = new DialogBox("The following tables were not removed:", list, "Data", DialogIcon.Warning);
                db1.ShowDialog();
            }
        }
        private void CMDataPreview(object sender, RoutedEventArgs e)
        {
            int count = listViewData.SelectedItems.Count;
            if (count == 0) return;

            List<string> titles = new List<string>();

            List<Data2D> data = new List<Data2D>();
            for (int i = 0; i < listViewData.SelectedItems.Count; i++)
            {
                Data2D d = (Data2D)listViewData.SelectedItems[i];
                data.Add(d);
                titles.Add(d.DataName);
            }

            DoDataPreview(data, titles, ColorScaleTypes.ThermalWarm);
        }
        private void CMDataPreviewScale(object sender, RoutedEventArgs e)
        {
            MenuItem mi = (MenuItem)sender;
            ColorScaleTypes type;

            try
            {
                type = EnumEx.FromDescription<ColorScaleTypes>(mi.Header.ToString());
            }
            catch (Exception)
            {
                type = ColorScaleTypes.ThermalWarm;
            }

            int count = listViewData.SelectedItems.Count;
            if (count == 0) return;

            List<string> titles = new List<string>();

            List<Data2D> data = new List<Data2D>();
            for (int i = 0; i < listViewData.SelectedItems.Count; i++)
            {
                Data2D d = (Data2D)listViewData.SelectedItems[i];
                data.Add(d);
                titles.Add(d.DataName);
            }

            DoDataPreview(data, titles, type);
        }
        private async void DoDataPreview(List<Data2D> data, List<string> titles, ColorScaleTypes type)
        {
            string title = TitleBuilder.Create(titles.ToArray<string>(), '-', 40);

            //Data2DDisplayTab it = new Data2DDisplayTab(data, type);
            DataDisplayTab it = new DataDisplayTab(type);

            ClosableTabItem cti = ClosableTabItem.Create(it, TabType.DataDisplay, title);
            tabMain.Items.Add(cti);
            tabMain.SelectedItem = cti;

            await it.AddDataSourceAsync(data);
        }

        private void CMDataSave(object sender, RoutedEventArgs e)
        {
            List<Data2D> toSave = new List<Data2D>();
            foreach (object obj in listViewData.SelectedItems)
            {
                Data2D d = obj as Data2D;
                if (obj != null)
                {
                    toSave.Add(d);
                }
            }

            if (toSave.Count == 0) return;

            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "Text Files (.txt)|*.txt";

            Nullable<bool> result = sfd.ShowDialog();
            if (result != true) return;

            if (toSave.Count == 1)
            {
                toSave[0].Save(sfd.FileName, FileType.CSV);
            }
            else if (toSave.Count > 1)
            {
                for (int i = 0; i < toSave.Count; i++)
                {
                    string savePath = sfd.FileName.Insert(sfd.FileName.Length - 4, string.Format("_{0}", i.ToString()));
                    toSave[i].Save(savePath, FileType.CSV);
                }
            }

            DialogBox db = new DialogBox("File(s) saved successfully!", sfd.FileName, "Save", DialogIcon.Ok);
            db.ShowDialog();
        }
        private void CMDataRename(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem;
            if (mi == null) return;

            Data2D d2d = listViewData.SelectedItem as Data2D;
            if (d2d == null) return;

            string currentTitle = d2d.DataName;

            TextEntryDialog te = new TextEntryDialog("Enter a new title:", currentTitle);
            if (te.ShowDialog() == true)
            {
                if (String.IsNullOrEmpty(te.EnteredText))
                {
                    DialogBox db = new DialogBox("Could not update the title of the selected object.",
                        "No text entered.", "Title", DialogIcon.Error);
                    db.ShowDialog();
                    return;
                }
                if (char.IsWhiteSpace(te.EnteredText, 0))
                {
                    DialogBox db = new DialogBox("Could not update the title of the selected object.",
                        "First character is a space.", "Title", DialogIcon.Error);
                    db.ShowDialog();
                    return;
                }

                listViewData.UnselectAll();

                d2d.DataName = te.EnteredText;
            }
        }
        private void CMDataProperties(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem;
            if (mi == null) return;

            Data2D d2d = listViewData.SelectedItem as Data2D;
            if (d2d == null) return;

            List<Point> locations = new List<Point>();

            string line1 = string.Format("Name: {0}", d2d.DataName);
            string line2 = string.Format("Dimensions: {0} x {1} pixels\n" +
                                         "Maximum :   {2} counts at:\n\n",
                                         d2d.Width, d2d.Height, d2d.GetMaximum(out locations));
            foreach (Point p in locations)
            {
                line2 += string.Format("(X: {0} Y:{1})\n", p.X, p.Y);
            }
            line2 = line2.Remove(line2.Length - 1, 1);

            DialogBox db = new DialogBox(line1, line2, "Properties", DialogIcon.Information);
            db.ShowDialog();
            return;
        }

        private async void CMSpecView(object sender, RoutedEventArgs e)
        {
            try
            {
                foreach (object obj in listViewSpectra.SelectedItems)
                {
                    Spectrum s = (Spectrum)obj;
                    if (s == null) continue;                    

                    if (s.SpectrumType == SpectrumType.Cameca1280 || s.SpectrumType == SpectrumType.CamecaNanoSIMS)
                    {
                        CamecaSpectrum cSpec = (CamecaSpectrum)s;

                        DataDisplayTab dt = new DataDisplayTab();
                        ClosableTabItem cti = ClosableTabItem.Create(dt, TabType.DataDisplay, cSpec.Name);
                        tabMain.Items.Add(cti);
                        tabMain.SelectedItem = cti;

                        pw = new ProgressWindow($"Generating images from spectrum {s.Name}. Please wait.", "Spectrum");
                        pw.Show();

                        int counter = 0;
                        int numSpec = cSpec.NumberSpecies;

                        foreach(CamecaSpecies species in cSpec.Species)
                        {
                            Data3D d = await cSpec.FromSpeciesAsync(species, cSpec.Name + " - " + species.Label);
                            await dt.AddDataSourceAsync(d);
                            pw.UpdateProgress(++counter * 100 / numSpec);
                        }

                        pw.Close();
                        pw = null;
                    }
                    else
                    {
                        SpectrumTab st = new SpectrumTab();
                        st.SetData(s.Name, s);

                        ClosableTabItem cti = ClosableTabItem.Create(st, TabType.Spectrum, s.Name);
                        tabMain.Items.Add(cti);
                        tabMain.SelectedItem = cti;
                    }           

                    Ribbon.SelectedIndex = 3;
                }
            }
            catch (Exception)
            {
                DialogBox db = new DialogBox("Could not open the selected spectra.",
                    "If you are trying to open multiple spectra, try opening them one at a time.",
                    "Spectra", DialogIcon.Error);
                db.ShowDialog();
                return;
            }
        }
        private void CMSpecDelete(object sender, RoutedEventArgs e)
        {
            if (listViewSpectra.SelectedItems.Count == 0) return;
            string msg = "";
            if (listViewSpectra.SelectedItems.Count == 1) msg = "Delete 1 spectrum?";
            else
            {
                msg = string.Format("Delete {0} spectra?", listViewSpectra.SelectedItems.Count);
            }
            DialogBox db = new DialogBox(msg,
                "Click OK to confirm or Cancel to return.", "Confirm", DialogIcon.Help, true);
            Nullable<bool> result = db.ShowDialog();
            if (result != true) return;

            Spectrum[] toRemove = new Spectrum[listViewSpectra.SelectedItems.Count];
            for (int i = 0; i < listViewSpectra.SelectedItems.Count; i++)
            {
                toRemove[i] = (Spectrum)listViewSpectra.SelectedItems[i];
            }
            List<KeyValuePair<Spectrum, string>> notRemoved = new List<KeyValuePair<Spectrum, string>>();
            for (int i = 0; i < toRemove.Length; i++)
            {
                try
                {
                    if (!Workspace.Spectra.Contains(toRemove[i])) throw new ArgumentException("Selected spectrum is not present in the collection.");
                    Workspace.Spectra.Remove(toRemove[i]);
                    toRemove[i].Dispose();
                }
                catch (ArgumentException ARex)
                {
                    notRemoved.Add(new KeyValuePair<Spectrum, string>(toRemove[i], ARex.Message));
                }
                catch (Exception ex)
                {
                    notRemoved.Add(new KeyValuePair<Spectrum, string>(toRemove[i], ex.Message));
                }
            }

            if (notRemoved.Count > 0)
            {
                string list = "";
                foreach (KeyValuePair<Spectrum, string> kvp in notRemoved)
                {
                    list += string.Format("{0}: {1}\n", kvp.Key.ToString(), kvp.Value);
                }
                list.Remove(list.Length - 2, 2);

                DialogBox db1 = new DialogBox("The following tables were not removed:", list, "Data", DialogIcon.Warning);
                db1.ShowDialog();
            }
        }
        private void CMSpecPCA(object sender, RoutedEventArgs e)
        {
            Spectrum s = listViewSpectra.SelectedItem as Spectrum;
            if (s == null) return;

            //ObservableCollection<Peak> peaks = Peak.Find(s.ToDoubleArray(), 10, null);

            PCATab pca = new PCATab();
            pca.OriginalSpectrum = s;
            ClosableTabItem cti = ClosableTabItem.Create(pca, TabType.PCA, $"PCA - {s.Name}");
            tabMain.Items.Add(cti);
            tabMain.SelectedItem = cti;
        }
        private void CMSpecRename(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem;
            if (mi == null) return;

            Spectrum s = listViewSpectra.SelectedItem as Spectrum;
            if (s == null) return;

            string currentTitle = s.Name;

            TextEntryDialog te = new TextEntryDialog("Enter a new title:", currentTitle);
            if (te.ShowDialog() == true)
            {
                if (String.IsNullOrEmpty(te.EnteredText))
                {
                    DialogBox db = new DialogBox("Could not update the title of the selected object.",
                        "No text entered.", "Title", DialogIcon.Error);
                    db.ShowDialog();
                    return;
                }
                if (char.IsWhiteSpace(te.EnteredText, 0))
                {
                    DialogBox db = new DialogBox("Could not update the title of the selected object.",
                        "First character is a space.", "Title", DialogIcon.Error);
                    db.ShowDialog();
                    return;
                }
                s.Name = te.EnteredText;
            }
        }
        private void CMSpecSave(object sender, RoutedEventArgs e)
        {
            Dictionary<Spectrum, string> notSaved = new Dictionary<Spectrum, string>();

            if (listViewSpectra.SelectedItems.Count == 0) return;

            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "Bio-ToF Retro Files (.xyt)|*.xyt";
            if (sfd.ShowDialog() != true) return;

            int counter = 0;
            foreach (object obj in listViewSpectra.SelectedItems)
            {
                Spectrum s = obj as Spectrum;
                if (s == null) continue;

                if (s.SpectrumType != SpectrumType.BioToF)
                {
                    notSaved.Add(s, "Invalid spectrum type. Must be Bio-ToF.");
                    continue;
                }

                string savePath = sfd.FileName.Insert(sfd.FileName.Length - 4,
                    string.Format("_{0}", (++counter).ToString("000")));
                BioToFSpectrum btSpec = s as BioToFSpectrum;

                try
                {
                    if (btSpec == null)
                    {
                        notSaved.Add(s, "Invalid spectrum type. Must be Bio-ToF");
                        continue;
                    }

                    btSpec.Save(savePath);
                }
                catch (Exception ex)
                {
                    notSaved.Add(btSpec, ex.Message);
                }
            }

            if (notSaved.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                foreach (Spectrum s in notSaved.Keys)
                {
                    sb.AppendLine(s.Name + " - " + notSaved[s]);
                }

                DialogBox.Show("The spectra listed below were unable to be saved. Spectra not listed were saved successfully.",
                    sb.ToString(), "Save", DialogIcon.Information);
                return;
            }
            else
            {
                DialogBox.Show("The spectra were saved successfully.", sfd.FileName, "Save", DialogIcon.Ok);
                return;
            }
        }
        private void CMSpecExport(object sender, RoutedEventArgs e)
        {
            Dictionary<Spectrum, string> notSaved = new Dictionary<Spectrum, string>();

            if (listViewSpectra.SelectedItems.Count == 0) return;

            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "Text Files (.txt)|*.txt";
            if (sfd.ShowDialog() != true) return;

            int counter = 0;
            foreach (object obj in listViewSpectra.SelectedItems)
            {
                Spectrum s = obj as Spectrum;
                if (s == null) continue;

                if (s.SpectrumType != SpectrumType.BioToF)
                {
                    notSaved.Add(s, "Invalid spectrum type. Must be Bio-ToF.");
                    continue;
                }

                string savePath = sfd.FileName.Insert(sfd.FileName.Length - 4,
                    string.Format("_{0}", (++counter).ToString("000")));
                BioToFSpectrum btSpec = s as BioToFSpectrum;

                try
                {
                    if (btSpec == null)
                    {
                        notSaved.Add(s, "Invalid spectrum type. Must be Bio-ToF");
                        continue;
                    }

                    btSpec.SaveText(savePath);
                }
                catch (Exception ex)
                {
                    notSaved.Add(btSpec, ex.Message);
                }
            }

            if (notSaved.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                foreach (Spectrum s in notSaved.Keys)
                {
                    sb.AppendLine(s.Name + " - " + notSaved[s]);
                }

                DialogBox.Show("The spectra listed below were unable to be saved. Spectra not listed were saved successfully.",
                    sb.ToString(), "Save", DialogIcon.Information);
                return;
            }
            else
            {
                DialogBox.Show("The spectra were saved successfully.", sfd.FileName, "Save", DialogIcon.Ok);
                return;
            }
        }

        private void CMCompEdit(object sender, RoutedEventArgs e)
        {
            if (listViewComponents.SelectedItems.Count == 0) return;

            ComponentTab ct = new ComponentTab();
            ct.DoUpdate((ImageComponent)listViewComponents.SelectedItem);
            ClosableTabItem cti = ClosableTabItem.Create(ct, TabType.Component,
                ((ImageComponent)listViewComponents.SelectedItem).ComponentName);
            tabMain.Items.Add(cti);
            tabMain.SelectedItem = cti;
        }
        private void CMCompDelete(object sender, RoutedEventArgs e)
        {
            if (listViewComponents.SelectedItems.Count == 0) return;
            string msg = "";
            if (listViewComponents.SelectedItems.Count == 1) msg = "Delete 1 component?";
            else
            {
                msg = string.Format("Delete {0} components?", listViewComponents.SelectedItems.Count);
            }
            DialogBox db = new DialogBox(msg,
                "Click OK to confirm or Cancel to return.", "Confirm", DialogIcon.Help, true);
            Nullable<bool> result = db.ShowDialog();
            if (result != true) return;

            ImageComponent[] toRemove = new ImageComponent[listViewComponents.SelectedItems.Count];
            for (int i = 0; i < listViewComponents.SelectedItems.Count; i++)
            {
                toRemove[i] = (ImageComponent)listViewComponents.SelectedItems[i];
            }
            List<KeyValuePair<ImageComponent, string>> notRemoved = new List<KeyValuePair<ImageComponent, string>>();
            for (int i = 0; i < toRemove.Length; i++)
            {
                try
                {
                    if (!Workspace.Components.Contains(toRemove[i])) throw new ArgumentException("Selected component is not present in the collection.");
                    Workspace.Components.Remove(toRemove[i]);
                }
                catch (ArgumentException ARex)
                {
                    notRemoved.Add(new KeyValuePair<ImageComponent, string>(toRemove[i], ARex.Message));
                }
                catch (Exception ex)
                {
                    notRemoved.Add(new KeyValuePair<ImageComponent, string>(toRemove[i], ex.Message));
                }
            }

            if (notRemoved.Count > 0)
            {
                string list = "";
                foreach (KeyValuePair<ImageComponent, string> kvp in notRemoved)
                {
                    list += string.Format("{0}: {1}\n", kvp.Key.ToString(), kvp.Value);
                }
                list.Remove(list.Length - 2, 2);

                DialogBox db1 = new DialogBox("The following components were not removed:", list, "Components", DialogIcon.Warning);
                db1.ShowDialog();
            }
        }
        private void CMCompRename(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem;
            if (mi == null) return;

            ImageComponent c = listViewComponents.SelectedItem as ImageComponent;
            if (c == null) return;

            string currentTitle = c.ComponentName;

            TextEntryDialog te = new TextEntryDialog("Enter a new title:", currentTitle);
            if (te.ShowDialog() == true)
            {
                if (String.IsNullOrEmpty(te.EnteredText))
                {
                    DialogBox db = new DialogBox("Could not update the title of the selected object.",
                        "No text entered.", "Title", DialogIcon.Error);
                    db.ShowDialog();
                    return;
                }
                if (char.IsWhiteSpace(te.EnteredText, 0))
                {
                    DialogBox db = new DialogBox("Could not update the title of the selected object.",
                        "First character is a space.", "Title", DialogIcon.Error);
                    db.ShowDialog();
                    return;
                }
                c.ComponentName = te.EnteredText;
            }
        }
        private async void CMVolumeViewData(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedVolumes();

            DataDisplayTab dt = new DataDisplayTab();
            ClosableTabItem cti = ClosableTabItem.Create(dt, TabType.DataDisplay, "Volume Preview");
            tabMain.Items.Add(cti);
            tabMain.SelectedItem = cti;

            foreach (var item in selected)
            {
                await dt.AddDataSourceAsync(item.Data, item.DataColor);
            }
        }
        private void CMVolumeDelete(object sender, RoutedEventArgs e)
        {
            if (listViewVolumes.SelectedItems.Count == 0) return;
            string msg = "";
            if (listViewVolumes.SelectedItems.Count == 1) msg = "Delete 1 volume?";
            else
            {
                msg = string.Format("Delete {0} volumes?", listViewVolumes.SelectedItems.Count);
            }
            DialogBox db = new DialogBox(msg,
                "Click OK to confirm or Cancel to return.", "Confirm", DialogIcon.Help, true);
            Nullable<bool> result = db.ShowDialog();
            if (result != true) return;

            Volume[] toRemove = new Volume[listViewVolumes.SelectedItems.Count];
            for (int i = 0; i < listViewVolumes.SelectedItems.Count; i++)
            {
                toRemove[i] = (Volume)listViewVolumes.SelectedItems[i];
            }
            List<KeyValuePair<Volume, string>> notRemoved = new List<KeyValuePair<Volume, string>>();
            for (int i = 0; i < toRemove.Length; i++)
            {
                try
                {
                    if (!Workspace.Volumes.Contains(toRemove[i])) throw new ArgumentException("Selected volume is not present in the collection.");
                    Workspace.Volumes.Remove(toRemove[i]);
                }
                catch (ArgumentException ARex)
                {
                    notRemoved.Add(new KeyValuePair<Volume, string>(toRemove[i], ARex.Message));
                }
                catch (Exception ex)
                {
                    notRemoved.Add(new KeyValuePair<Volume, string>(toRemove[i], ex.Message));
                }
            }

            if (notRemoved.Count > 0)
            {
                string list = "";
                foreach (KeyValuePair<Volume, string> kvp in notRemoved)
                {
                    list += string.Format("{0}: {1}\n", kvp.Key.ToString(), kvp.Value);
                }
                list.Remove(list.Length - 2, 2);

                DialogBox db1 = new DialogBox("The following volumes were not removed:", list, "Volumes", DialogIcon.Warning);
                db1.ShowDialog();
            }
        }
        private void CMVolumeRename(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem;
            if (mi == null) return;

            Volume v = listViewVolumes.SelectedItem as Volume;
            if (v == null) return;

            string currentTitle = v.VolumeName;

            TextEntryDialog te = new TextEntryDialog("Enter a new title:", currentTitle);
            if (te.ShowDialog() == true)
            {
                if (String.IsNullOrEmpty(te.EnteredText))
                {
                    DialogBox db = new DialogBox("Could not update the title of the selected object.",
                        "No text entered.", "Title", DialogIcon.Error);
                    db.ShowDialog();
                    return;
                }
                if (char.IsWhiteSpace(te.EnteredText, 0))
                {
                    DialogBox db = new DialogBox("Could not update the title of the selected object.",
                        "First character is a space.", "Title", DialogIcon.Error);
                    db.ShowDialog();
                    return;
                }
                v.VolumeName = te.EnteredText;
            }
        }

        private void CMImgsView(object sender, RoutedEventArgs e)
        {
            if (listViewImageSeries.SelectedItems.Count == 0) return;

            foreach (object obj in listViewImageSeries.SelectedItems)
            {
                DisplaySeries series = (DisplaySeries)obj;
                if (obj == null) continue;

                DisplayTab it = new DisplayTab();
                it.CurrentSeries = series;

                ClosableTabItem cti = ClosableTabItem.Create(it, TabType.Display, series.SeriesName);
                tabMain.Items.Add(cti);
                tabMain.SelectedItem = cti;
            }
        }
        private void CMImgsDelete(object sender, RoutedEventArgs e)
        {
            if (listViewImageSeries.SelectedItems.Count == 0) return;
            string msg = "";
            if (listViewImageSeries.SelectedItems.Count == 1) msg = "Delete 1 image series?";
            else
            {
                msg = string.Format("Delete {0} image series?", listViewImageSeries.SelectedItems.Count);
            }
            DialogBox db = new DialogBox(msg,
                "Click OK to confirm or Cancel to return.", "Confirm", DialogIcon.Help, true);
            Nullable<bool> result = db.ShowDialog();
            if (result != true) return;

            DisplaySeries[] toRemove = new DisplaySeries[listViewImageSeries.SelectedItems.Count];
            for (int i = 0; i < listViewImageSeries.SelectedItems.Count; i++)
            {
                toRemove[i] = (DisplaySeries)listViewImageSeries.SelectedItems[i];
            }
            List<KeyValuePair<DisplaySeries, string>> notRemoved = new List<KeyValuePair<DisplaySeries, string>>();
            for (int i = 0; i < toRemove.Length; i++)
            {
                try
                {
                    if (!Workspace.ImageSeries.Contains(toRemove[i])) throw new ArgumentException("Selected image series is not present in the collection.");
                    Workspace.ImageSeries.Remove(toRemove[i]);
                }
                catch (ArgumentException ARex)
                {
                    notRemoved.Add(new KeyValuePair<DisplaySeries, string>(toRemove[i], ARex.Message));
                }
                catch (Exception ex)
                {
                    notRemoved.Add(new KeyValuePair<DisplaySeries, string>(toRemove[i], ex.Message));
                }
            }

            if (notRemoved.Count > 0)
            {
                string list = "";
                foreach (KeyValuePair<DisplaySeries, string> kvp in notRemoved)
                {
                    list += string.Format("{0}: {1}\n", kvp.Key.ToString(), kvp.Value);
                }
                list.Remove(list.Length - 2, 2);

                DialogBox db1 = new DialogBox("The following image series were not removed:", list, "Image Series", DialogIcon.Warning);
                db1.ShowDialog();
            }
        }
        private void CMImgsRename(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem;
            if (mi == null) return;

            DisplaySeries ds = listViewImageSeries.SelectedItem as DisplaySeries;
            if (ds == null) return;

            string currentTitle = ds.SeriesName;

            TextEntryDialog te = new TextEntryDialog("Enter a new title:", currentTitle);
            if (te.ShowDialog() == true)
            {
                if (String.IsNullOrEmpty(te.EnteredText))
                {
                    DialogBox db = new DialogBox("Could not update the title of the selected object.",
                        "No text entered.", "Title", DialogIcon.Error);
                    db.ShowDialog();
                    return;
                }
                if (char.IsWhiteSpace(te.EnteredText, 0))
                {
                    DialogBox db = new DialogBox("Could not update the title of the selected object.",
                        "First character is a space.", "Title", DialogIcon.Error);
                    db.ShowDialog();
                    return;
                }
                ds.SeriesName = te.EnteredText;
            }
        }
        private void CMISEMView(object sender, RoutedEventArgs e)
        {
            if (listViewSEMs.SelectedItems.Count == 0) return;

            SEMTab st = new SEMTab();
            foreach (object obj in listViewSEMs.SelectedItems)
            {
                SEM sem = (SEM)obj;
                if (sem == null) continue;

                st.SEMImages.Add(sem);
            }

            ClosableTabItem cti = ClosableTabItem.Create(st, TabType.SEM);
            tabMain.Items.Add(cti);
            tabMain.SelectedItem = cti;
        }
        private void CMSEMDelete(object sender, RoutedEventArgs e)
        {
            if (listViewSEMs.SelectedItems.Count == 0) return;
            string msg = "";
            if (listViewSEMs.SelectedItems.Count == 1) msg = "Delete 1 SEM image?";
            else
            {
                msg = string.Format("Delete {0} SEM images?", listViewSEMs.SelectedItems.Count);
            }
            DialogBox db = new DialogBox(msg,
                "Click OK to confirm or Cancel to return.", "Confirm", DialogIcon.Help, true);
            Nullable<bool> result = db.ShowDialog();
            if (result != true) return;

            SEM[] toRemove = new SEM[listViewSEMs.SelectedItems.Count];
            for (int i = 0; i < listViewSEMs.SelectedItems.Count; i++)
            {
                toRemove[i] = (SEM)listViewSEMs.SelectedItems[i];
            }
            List<KeyValuePair<SEM, string>> notRemoved = new List<KeyValuePair<SEM, string>>();
            for (int i = 0; i < toRemove.Length; i++)
            {
                try
                {
                    if (!Workspace.SEMs.Contains(toRemove[i])) throw new ArgumentException("Selected SEM image is not present in the collection.");
                    Workspace.SEMs.Remove(toRemove[i]);
                }
                catch (ArgumentException ARex)
                {
                    notRemoved.Add(new KeyValuePair<SEM, string>(toRemove[i], ARex.Message));
                }
                catch (Exception ex)
                {
                    notRemoved.Add(new KeyValuePair<SEM, string>(toRemove[i], ex.Message));
                }
            }

            if (notRemoved.Count > 0)
            {
                string list = "";
                foreach (KeyValuePair<SEM, string> kvp in notRemoved)
                {
                    list += string.Format("{0}: {1}\n", kvp.Key.ToString(), kvp.Value);
                }
                list.Remove(list.Length - 2, 2);

                DialogBox db1 = new DialogBox("The following SEM images were not removed:", list, "SEM", DialogIcon.Warning);
                db1.ShowDialog();
            }
        }
        private void CMSEMRename(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem;
            if (mi == null) return;

            SEM sem = listViewSEMs.SelectedItem as SEM;
            if (sem == null) return;

            string currentTitle = sem.SEMName;

            TextEntryDialog te = new TextEntryDialog("Enter a new title:", currentTitle);
            if (te.ShowDialog() == true)
            {
                if (String.IsNullOrEmpty(te.EnteredText))
                {
                    DialogBox db = new DialogBox("Could not update the title of the selected object.",
                        "No text entered.", "Title", DialogIcon.Error);
                    db.ShowDialog();
                    return;
                }
                if (char.IsWhiteSpace(te.EnteredText, 0))
                {
                    DialogBox db = new DialogBox("Could not update the title of the selected object.",
                        "First character is a space.", "Title", DialogIcon.Error);
                    db.ShowDialog();
                    return;
                }
                sem.SEMName = te.EnteredText;
            }
        }

        private void listViewData_Drop(object sender, DragEventArgs e)
        {
            DisplayImage di = (DisplayImage)e.Data.GetData("DisplayImage");
            if (di == null) return;

            BitmapSource bs = (BitmapSource)di.Source;
            if (bs == null) return;

            Data2D d = ImageGenerator.Instance.ConvertToData2D(bs);
            d.DataName = di.Title;
            Workspace.Data.Add(d);
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            ScrollViewer scv = (ScrollViewer)sender;
            scv.ScrollToVerticalOffset(scv.VerticalOffset - e.Delta);
            e.Handled = true;
        }

        private void listViewItemDataMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                ListViewItem lvi = sender as ListViewItem;
                if (lvi == null) return;

                Data2D d = lvi.Content as Data2D;
                DataObject obj = new DataObject("Data2D", d);
                DragDrop.DoDragDrop(lvi, obj, DragDropEffects.Copy);
            }
        }
        private void listViewItemVolumesMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var lvi = sender as ListViewItem;
                if (lvi == null) return;

                var volume = lvi.Content as Volume;
                DataObject obj = new DataObject("Volume", volume);
                DragDrop.DoDragDrop(lvi, obj, DragDropEffects.Copy);
            }
        }
        #endregion

        #region Status Bar

        public static readonly DependencyProperty StatusBarTextProperty = DependencyProperty.Register("StatusBarText",
            typeof(string), typeof(MainWindow));
        public string StatusBarText
        {
            get { return (string)GetValue(StatusBarTextProperty); }
            set { SetValue(StatusBarTextProperty, value); }
        }

        private void StatusUpdated(object sender, StatusUpdatedRoutedEventArgs e)
        {
            StatusBarText = e.Message;
            Trace.WriteLine(e.Message);
        }

        private void statusBarAnimation_Completed(object sender, EventArgs e)
        {
            StatusBarText = string.Empty;
        }
        #endregion

        #region TabControl
        private void tabItem_MouseMove(object sender, MouseEventArgs e)
        {
            var tabItem = e.Source as TabItem;
            if (tabItem == null) return;

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragDrop.DoDragDrop(tabItem, tabItem, DragDropEffects.Move);
            }
        }
        private void tabItem_Drop(object sender, DragEventArgs e)
        {
            var tabItemTarget = e.Source as ClosableTabItem;
            var tabItemSource = e.Data.GetData(typeof(ClosableTabItem)) as ClosableTabItem;

            if (tabItemTarget == null || tabItemSource == null) return;
            if (tabItemTarget == tabItemSource) return;

            int sourceIndex = tabMain.Items.IndexOf(tabItemSource);
            int targetIndex = tabMain.Items.IndexOf(tabItemTarget);

            tabMain.Items.Remove(tabItemSource);
            tabMain.Items.Insert(targetIndex, tabItemSource);
            tabMain.SelectedItem = tabItemSource;
        }
        private void tabMain_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }
        #endregion

        #region Registration
        private void ribbonOpenTransformTab_Click(object sender, RoutedEventArgs e)
        {
            RibbonButton button = sender as RibbonButton;
            if (button == null) return;

            DataRegistrationTab drt = new DataRegistrationTab();

            if (button == ribbonbuttonOpenTransformFromFusion)
            {
                ClosableTabItem cti = tabMain.SelectedItem as ClosableTabItem;
                if (cti == null) return;

                FusionTab ft = cti.Content as FusionTab;
                if (ft == null)
                {
                    DialogBox.Show("Not a valid fusion document.",
                        "Navigate to an open fusion document and try again.", "Transform", DialogIcon.Error);
                    return;
                }

                if (ft.HighResImage.ImageSource == null || ft.LowResImage.ImageSource == null)
                {
                    DialogBox.Show("One or more input images is missing.",
                        "Make sure a moving image and fixed image are loaded and try again.", "Transform", DialogIcon.Error);
                    return;
                }

                if (!ft.IsRegistered)
                {
                    DialogBox.Show("Images are not registered.",
                        "Register images to generate a transform and then try again.", "Transform", DialogIcon.Error);
                    return;
                }

                drt.SetTransform(ft.RegistrationResults);
                drt.SetRegisteredImages(ft.HighResImage.ImageSource, ft.LowResImage.ImageSource);
            }

            ClosableTabItem ctiDrt = ClosableTabItem.Create(drt, TabType.DataRegistration, "Data Transform");
            tabMain.Items.Add(ctiDrt);
            tabMain.SelectedItem = ctiDrt;
        }
        #endregion

        #region Testing
#pragma warning disable 1998
        private async void test1_Click(object sender, RoutedEventArgs e)
        {
            Spectrum s = listViewSpectra.SelectedItem as Spectrum;

            float[] masses;
            uint[] intensities = s.GetSpectrum(out masses);

            using (StreamWriter sw = new StreamWriter(@"D:\spec.txt"))
            {
                for (int i = 0; i < masses.Length; i++)
                {
                    sw.WriteLine($"{masses[i]},{intensities[i]},");
                }
            }
        }
        private async void test2_Click(object sender, RoutedEventArgs e)
        {
            if (Workspace.Spectra.Count == 0) return;

            BioToFSpectrum bts = Workspace.Spectra[0] as BioToFSpectrum;
            if (bts == null) return;

            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "Bio-ToF Retro File (.xyt)|*.xyt";
            if (sfd.ShowDialog() != true) return;

            bts.Save(sfd.FileName);
        }
        private async void test3_Click(object sender, RoutedEventArgs e)
        {
            if (Workspace.Spectra.Count == 0) return;

            BioToFSpectrum bts = Workspace.Spectra[0] as BioToFSpectrum;
            if (bts == null) return;

            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "Bio-ToF Header File (.hdr)|*.hdr";
            if (sfd.ShowDialog() != true) return;

            bts.SaveHeader(sfd.FileName);
        }
        private async void test4_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "Text Files (.txt)|*.txt";
            ofd.Multiselect = true;

            if (ofd.ShowDialog() != true) return;

            List<Data2D> readIn = new List<Data2D>();

            var filesToLoad = ofd.FileNames;

            foreach (var file in filesToLoad)
            {
                using (StreamReader sr = new StreamReader(file))
                {
                    string[] parameters = sr.ReadLine().Split('\t');

                    int sizeX = int.Parse(parameters[0]);
                    int sizeY = int.Parse(parameters[1]);
                    int fovNumber = int.Parse(parameters[2]);
                    int stageX = int.Parse(parameters[3]);
                    int stageY = int.Parse(parameters[4]);
                    int fileType = int.Parse(parameters[5]);

                    float[,] matrix = new float[sizeX, sizeY];

                    for (int y = 0; y < sizeY; y++)
                    {
                        string[] parts = sr.ReadLine().Split('\t');
                        for (int x = 0; x < sizeX; x++)
                        {
                            matrix[x, y] = float.Parse(parts[x]);
                        }
                    }

                    Data2D d = new Data2D(matrix);
                    d.DataName = Path.GetFileNameWithoutExtension(file);

                    readIn.Add(d);

                    //float mean = d.NonSparseMean;
                    //float stdDev = d.NonSparseStdDev;

                    //float cutoff = mean;// - 2 * stdDev;

                    //Data2D binary = new Data2D(d.Width, d.Height);
                    //for (int x = 0; x < d.Width; x++)
                    //{
                    //    for (int y = 0; y < d.Height; y++)
                    //    {
                    //        binary[x, y] = d[x, y] >= cutoff ? 1.0f : 0.0f;
                    //    }
                    //}
                    //binary.DataName = "Binary: " + d.DataName;

                    //readIn.Add(binary);
                }
            }

            DataDisplayTab dt = new DataDisplayTab(ColorScaleTypes.ThermalCold);
            ClosableTabItem cti = ClosableTabItem.Create(dt, TabType.DataDisplay, "Data");
            tabMain.Items.Add(cti);
            tabMain.SelectedItem = cti;

            foreach (var d in readIn)
            {
                await dt.AddDataSourceAsync(d);
            }
        }
        private async void test5_Click(object sender, RoutedEventArgs e)
        {
            int numBoxes = 6;
            int sizeX = 256;
            int sizeY = 256;
            int boxSizeX = 50;
            int boxSizeY = 50;
            int minBoxCounts = 50;
            int maxBoxCounts = 100;
            int minSubstrateCounts = 25;
            int maxSubstrateCounts = 40;

            Random rand = new Random();

            List<Point> centers = new List<Point>()
            {
                new Point(65, 35),
                new Point(125, 35),
                new Point(190, 35),

                new Point(65, 135),
                new Point(125, 135),
                new Point(190, 135),
            };

            List<double> masses = new List<double>()
            {
                10d, 25d, 50d, 75d, 100d, 150d, 5d
            };

            bool[,] hasSquare = new bool[sizeX, sizeY];

            // Create mass channels
            List<Data2D> massChannels = new List<Data2D>();
            // Create substrate channel
            Data2D substrate = new Data2D(sizeX, sizeY);
            Data2D ideal = new Data2D(sizeX, sizeY);

            for (int i = 0; i < numBoxes; i++)
            {
                int[,] temp = new int[boxSizeX, boxSizeY];

                for (int x = 0; x < boxSizeX; x++)
                {
                    for (int y = 0; y < boxSizeY; y++)
                    {
                        temp[x, y] = rand.Next(minBoxCounts, maxBoxCounts);
                    }
                }

                int[,] matrix = new int[boxSizeX, boxSizeY];
                for (int x = 0; x < boxSizeX; x++)
                {
                    for (int y = 0; y < boxSizeY; y++)
                    {
                        int sum = 0;
                        int startX = x - 3;
                        int startY = y - 3;

                        for (int a = 0; a < 7; a++)
                        {
                            for (int b = 0; b < 7; b++)
                            {
                                int xx = startX + a;
                                int yy = startY + b;

                                if (xx < 0 || xx >= boxSizeX ||
                                    yy < 0 || yy >= boxSizeY) continue;

                                sum += temp[xx, yy];
                            }
                        }

                        matrix[x, y] = (int)(sum / 49d);
                    }
                }

                Data2D massChannel = new Data2D(sizeX, sizeY);

                int boxStartX = (int)centers[i].X - (boxSizeX / 2);
                int boxStartY = (int)centers[i].Y - (boxSizeY / 2);

                for (int x = 0; x < boxSizeX; x++)
                {
                    for (int y = 0; y < boxSizeY; y++)
                    {
                        massChannel[boxStartX + x, boxStartY + y] = matrix[x, y];
                        hasSquare[boxStartX + x, boxStartY + y] = true;

                        ideal[boxStartX + x, boxStartY + y] = maxBoxCounts;

                        if (temp[x, y] - matrix[x, y] > 0)
                            substrate[boxStartX + x, boxStartY + y] += temp[x, y] - matrix[x, y];
                    }
                }

                massChannels.Add(massChannel);
            }

            // Fill in rest of subtrate channel
            for (int x = 0; x < sizeX; x++)
            {
                for (int y = 0; y < sizeY; y++)
                {
                    if (!hasSquare[x, y])
                    {
                        substrate[x, y] += rand.Next(minSubstrateCounts, maxSubstrateCounts);
                        ideal[x, y] = maxSubstrateCounts;
                    }
                }
            }

            massChannels.Add(substrate);

            SimulatedCameca1280Spectrum spec = new SimulatedCameca1280Spectrum();
            foreach (var mass in masses)
            {
                CamecaSpecies species = new CamecaSpecies()
                {
                    Mass = mass,
                    Label = $"Mass {mass.ToString("0.00")}"
                };
                spec.Species.Add(species);
            }

            spec.SetMatrixData(massChannels);

            AvailableHost.AvailableSpectraSource.AddSpectrum(spec);


            Data2D totalIon = new Data2D(sizeX, sizeY)
            {
                DataName = "Total Ion"
            };
            foreach (var d in massChannels)
            {
                totalIon += d;
            }

            var upscaled = ideal.Resize(totalIon.Width * 2, totalIon.Height * 2);
            upscaled.DataName = "Upscalaed";

            var enhanced = Filter.DoFilter(upscaled, FilterType.MedianSmooth);
            enhanced.DataName = "Enhanced";

            AvailableHost.AvailableTablesSource.AddTable(enhanced);
            AvailableHost.AvailableTablesSource.AddTable(upscaled);
        }
        public class SimulatedCameca1280Spectrum : Cameca1280Spectrum
        {
            public SimulatedCameca1280Spectrum()
                : base("Simulated")
            {
                _species = new List<CamecaSpecies>();
            }

            public void SetMatrixData(List<Data2D> data)
            {
                _matrix = new List<Data2D[]>()
                {
                    data.ToArray()
                };

                _sizeX = data[0].Width;
                _sizeY = data[0].Height;
                _sizeZ = 1;

                _intensities = GetSpectrum(out _masses);

                _startMass = (float)_species.Min(s => s.Mass);
                _endMass = (float)_species.Max(s => s.Mass);
            }
        }
        private double[,] GuidedFilter(double[,] guide, double[,] matrix, int r = 2, double eps = 0.05)
        {
            int sizeX = guide.GetLength(0);
            int sizeY = guide.GetLength(1);

            double[,] meanG = new double[sizeX, sizeY];
            double[,] meanM = new double[sizeX, sizeY];
            double[,] meanGM = new double[sizeX, sizeY];
            double[,] meanGG = new double[sizeX, sizeY];

            for (int x = 0 ; x < sizeX; x++)
            {
                for (int y = 0; y < sizeY; y++)
                {
                    int bcArea = 0;

                    for (int xx = x - r; xx <= x + r; xx++)
                    {
                        if (xx < 0 || xx >= sizeX) continue;
                        for (int yy = y - r; yy <= y + r; yy++)
                        {
                            if (yy < 0 || yy >= sizeY) continue;

                            meanG[x, y] += guide[xx, yy];
                            meanM[x, y] += matrix[xx, yy];
                            meanGM[x, y] += guide[xx, yy] * matrix[xx, yy];
                            meanGG[x, y] += guide[xx, yy] * guide[xx, yy];

                            bcArea++;
                        }
                    }

                    meanG[x, y] /= bcArea;
                    meanM[x, y] /= bcArea;
                    meanGM[x, y] /= bcArea;
                    meanGG[x, y] /= bcArea;
                }
            }

            var covGM = Elementwise.Subtract(meanGM, Elementwise.Multiply(meanG, meanM));
            var varG = Elementwise.Subtract(meanGG, Elementwise.Multiply(meanG, meanG));

            var a = Elementwise.Divide(covGM, varG.Add(eps));
            var b = Elementwise.Subtract(meanM, Elementwise.Multiply(a, meanG));

            double[,] meanA = new double[sizeX, sizeY];
            double[,] meanB = new double[sizeX, sizeY];

            for (int x = 0 ; x < sizeX; x++)
            {
                for (int y = 0; y < sizeY; y++)
                {
                    int bcArea = 0;

                    for (int xx = x - r; xx <= x + r; xx++)
                    {
                        if (xx < 0 || xx >= sizeX) continue;
                        for (int yy = y - r; yy <= y + r; yy++)
                        {
                            if (yy < 0 || yy >= sizeY) continue;

                            meanA[x, y] += a[xx, yy];
                            meanB[x, y] += b[xx, yy];

                            bcArea++;
                        }
                    }

                    meanA[x, y] /= bcArea;
                    meanB[x, y] /= bcArea;
                }
            }

            var q = Elementwise.Add(Elementwise.Multiply(meanA, guide), meanB);

            return  q;
        }
        private double[] SoftThreshold(double[] array, double lambda)
        {
            double th = lambda / 2;
            double[] temp = new double[array.Length];

            for (int i = 0; i < array.Length; i++)
            {
                if (array[i] > th)
                    temp[i] = array[i] - th;
                else if (array[i] < -th)
                    temp[i] = array[i] + th;
                else
                    temp[i] = 0;
            }

            return temp;
        }

        private async void test6_Click(object sender, RoutedEventArgs e)
        {
            var pan = AvailableHost.AvailableTablesSource.GetAvailableTables()[0];
            pan /= pan.Maximum;

            var spec = AvailableHost.AvailableSpectraSource.GetAvailableSpectra()[0] as Cameca1280Spectrum;

            var masses = new List<Data2D>();
            foreach (var species in spec.Species)
            {
                masses.Add(spec.FromSpecies(species));
            }

            int lrSizeX = spec.SizeX;
            int lrSizeY = spec.SizeY;
            int lrPixels = spec.SizeX * spec.SizeY;
            int hrSizeX = pan.Width;
            int hrSizeY = pan.Height;
            int hrPixels = pan.Width * pan.Height;
            int numMasses = masses.Count;

            // Normalize each mass to [0,1]
            double[,] hsMatrix = new double[lrPixels, numMasses];
            for (int i = 0; i < numMasses; i++)
            {
                for (int x = 0; x < lrSizeX; x++)
                {
                    for (int y = 0; y < lrSizeY; y++)
                    {
                        hsMatrix[x * lrSizeY + y, i] = masses[i][x, y];// / masses[i].Maximum;
                    }
                }
            }
            double[,] panArray = new double[hrSizeX, hrSizeY];
            for (int x = 0; x < hrSizeX; x++)
            {
                for (int y = 0; y < hrSizeY; y++)
                {
                    panArray[x, y] = pan[x, y];
                }
            }

            // Mean center the columns
            double[] means = new double[numMasses];
            for (int i = 0; i < numMasses; i++)
            {
                double sum = 0;
                for (int j = 0; j < lrPixels; j++)
                {
                    sum += hsMatrix[j, i];
                }
                means[i] = sum / lrPixels;
                for (int j = 0; j < lrPixels; j++)
                {
                    hsMatrix[j, i] -= means[i];
                }
            }

            var svd = new SingularValueDecomposition(hsMatrix, true, true, false, false);
            var u = svd.LeftSingularVectors;
            var d = svd.Diagonal;

            double[] pctExplained = new double[numMasses];
            double sumSv = d.Sum();
            for (int i = 0; i < numMasses; i++)
            {
                pctExplained[i] = d[i] / sumSv;
            }

            double sumPct = 0;
            int p = 0;
            for (int i = 0; i < numMasses; i++)
            {
                sumPct += pctExplained[i];
                if (sumPct >= 0.75)
                {
                    p = i;
                    break;
                }
            }

            double[,] upsampledMatrix = new double[hrPixels, numMasses];

            double[] crossCorrelations = new double[numMasses];
            Parallel.For(0, numMasses, i =>
            {
                double[,] pcReshaped = new double[lrSizeX, lrSizeY];
                for (int x = 0; x < lrSizeX; x++)
                {
                    for (int y = 0; y < lrSizeY; y++)
                    {
                        pcReshaped[x, y] = u[x * lrSizeY + y, i];
                    }
                }

                var upscaled = pcReshaped.Upscale(hrSizeX, hrSizeY, false);

                double numerator = 0;
                double sHigh = 0;
                double sLow = 0;
                double meanPc = upscaled.Sum() / upscaled.Length;
                double meanPan = pan.Mean;

                for (int x = 0; x < hrSizeX; x++)
                {
                    for (int y = 0; y < hrSizeY; y++)
                    {
                        numerator += (upscaled[x, y] - meanPc) * (pan[x, y] - meanPan);
                        sHigh += (pan[x, y] - meanPan) * (pan[x, y] - meanPan);
                        sLow += (upscaled[x, y] - meanPc) * (upscaled[x, y] - meanPc);
                    }
                }

                crossCorrelations[i] = numerator / Math.Sqrt(sHigh * sLow);
            });

            int pcIndex = 0;
            bool pcIsNeg = false;
            double pcMax = 0;
            for (int i = 0; i < numMasses; i++)
            {
                if(Math.Abs(crossCorrelations[i]) > pcMax)
                {
                    pcMax = Math.Abs(crossCorrelations[i]);
                    pcIndex = i;
                    pcIsNeg = crossCorrelations[i] < 0;
                }
            }

            double[,] pcs = new double[hrPixels, numMasses];

            List<Data2D> pcImages = new List<Data2D>();

            Parallel.For(0, numMasses, i =>
            {
                double[,] pcReshaped = new double[lrSizeX, lrSizeY];
                for (int x = 0; x < lrSizeX; x++)
                {
                    for (int y = 0; y < lrSizeY; y++)
                    {
                        pcReshaped[x, y] = u[x * lrSizeY + y, i];
                    }
                }
                var upscaled = pcReshaped.Upscale(hrSizeX, hrSizeY, false);

                Data2D upscaledData = new Data2D(hrSizeX, hrSizeY)
                {
                    DataName = $"PC {i}"
                };

                for (int x = 0; x < hrSizeX; x++)
                {
                    for (int y = 0; y < hrSizeY; y++)
                    {
                        upscaledData[x, y] = (float)upscaled[x, y];
                    }
                }

                pcImages.Add(upscaledData);                  

                //var gfpc = GuidedFilter(matched, upscaled, 2, 0.1);
                //for (int x = 0; x < hrSizeX; x++)
                //{
                //    for (int y = 0; y < hrSizeY; y++)
                //    {
                //        pcs[x * hrSizeY + y, i] = upscaled[x, y];
                //        //upsampledMatrix[x * hrSizeY + y, i] = gfpc[x, y];
                //        upsampledMatrix[x * hrSizeY + y, i] = matched[x, y];
                //    }
                //}

                if (i == pcIndex)
                //if(i <= p)
                {
                    var matched = HistogramMatching.Match((double[,])pan, upscaled, 1000);

                    //bool isNeg = crossCorrelations[i] < 0;
                    for (int x = 0; x < hrSizeX; x++)
                    {
                        for (int y = 0; y < hrSizeY; y++)
                        {
                            pcs[x * hrSizeY + y, i] = upscaled[x, y];

                            upscaled[x, y] = pcIsNeg ? upscaled[x, y] - matched[x, y] : upscaled[x, y] + matched[x, y];
                            //upscaled[x, y] = pcIsNeg ? -matched[x, y] : matched[x, y];
                            //upscaled[x, y] = isNeg ? -matched[x, y] : matched[x, y];
                        }
                    }
                }

                for (int x = 0; x < hrSizeX; x++)
                {
                    for (int y = 0; y < hrSizeY; y++)
                    {
                        pcs[x * hrSizeY + y, i] = upscaled[x, y];
                        upsampledMatrix[x * hrSizeY + y, i] = upscaled[x, y];
                    }
                }
            });


            //Parallel.For(0, numMasses, i =>
            //{
            //    double[,] pcReshaped = new double[lrSizeX, lrSizeY];
            //    for (int x = 0; x < lrSizeX; x++)
            //    {
            //        for (int y = 0; y < lrSizeY; y++)
            //        {
            //            pcReshaped[x, y] = u[x * lrSizeY + y, i];
            //        }
            //    }

            //    var upscaled = pcReshaped.Upscale(hrSizeX, hrSizeY, false);
            //    double[] hrPc = new double[hrPixels];

            //    if (i == 0)
            //    {
            //        for (int x = 0; x < hrSizeX; x++)
            //        {
            //            for (int y = 0; y < hrSizeY; y++)
            //            {
            //                hrPc[x * hrSizeY + y] = (pan[x, y] + upscaled[x, y]) / 2;
            //            }
            //        }
            //    }
            //    else
            //    {
            //        for (int x = 0; x < hrSizeX; x++)
            //        {
            //            for (int y = 0; y < hrSizeY; y++)
            //            {
            //                hrPc[x * hrSizeY + y] = upscaled[x, y];
            //            }
            //        }
            //    }


            //    for (int j = 0; j < hrPixels; j++)
            //    {
            //        upsampledMatrix[j, i] = hrPc[j];
            //    }

            //});

            var reverted = upsampledMatrix.Dot(svd.DiagonalMatrix).DotWithTransposed(svd.RightSingularVectors);

            // Revert mean centering
            for (int i = 0; i < hrPixels; i++)
            {
                for (int j = 0; j < numMasses; j++)
                {
                    reverted[i, j] += means[j];
                }
            }

            List<Data2D> fused = new List<Data2D>();
            for (int i = 0; i < numMasses; i++)
            {
                Data2D f = new Data2D(hrSizeX, hrSizeY)
                {
                    DataName = masses[i].DataName + "-Fused"
                };

                for (int x = 0; x < hrSizeX; x++)
                {
                    for (int y = 0; y < hrSizeY; y++)
                    {
                        f[x, y] = (float)reverted[x * hrSizeY + y, i];
                    }
                }

                fused.Add(f);
            }

            //List<Data2D> pcImages = new List<Data2D>();
            //for (int i = 0; i < numMasses; i++)
            //{
            //    Data2D pc = new Data2D(hrSizeX, hrSizeY)
            //    {
            //        DataName = $"PC {i}"
            //    };

            //    for (int x = 0; x < hrSizeX; x++)
            //    {
            //        for (int y = 0; y < hrSizeY; y++)
            //        {
            //            pc[x, y] = (float)pcs[x * hrSizeY + y, i];
            //        }
            //    }

            //    pcImages.Add(pc);
            //}

            var sortedPcs = pcImages.OrderBy(pc => pc.DataName);

            AvailableHost.AvailableTablesSource.AddTables(fused);
            AvailableHost.AvailableTablesSource.AddTables(sortedPcs);

            var dt = new DataDisplayTab();
            var cti = ClosableTabItem.Create(dt, TabType.DataDisplay, "GFPCA");
            tabMain.Items.Add(cti);
            tabMain.SelectedItem = cti;
            foreach (var f in fused)
            {
                await dt.AddDataSourceAsync(f);
            }
            
        }
        // GFPCA:
        //double[,] pcResahped = new double[lrSizeX, lrSizeY];
        //for (int x = 0; x<lrSizeX; x++)
        //{
        //    for (int y = 0; y<lrSizeY; y++)
        //    {
        //        int index = x * lrSizeY + y;
        //pcResahped[x, y] = u[index, i];
        //    }
        //}

        //var upscaled = pcResahped.Upscale(hrSizeX, hrSizeY, false);
        //double[] hrPc = new double[hrPixels];

        //// If PC number is less than p, upscale and do guided filter
        //if (i <= p)
        //{
        //    // Do guided filter
        //    var gfpc = GuidedFilter(panArray, upscaled, 2, 0.5);
        //    for (int x = 0; x<hrSizeX; x++)
        //    {
        //        for (int y = 0; y<hrSizeY; y++)
        //        {
        //            int index = x * hrSizeY + y;
        //hrPc[index] = gfpc[x, y];
        //        }
        //    }
        //}

        //// Otherwise just upscale and threshold
        //else
        //{
        //    for (int x = 0; x<hrSizeX; x++)
        //    {
        //        for (int y = 0; y<hrSizeY; y++)
        //        {
        //            int index = x * hrSizeY + y;
        //hrPc[index] = upscaled[x, y];
        //        }
        //    }

        //    //hrPc = SoftThreshold(hrPc, 0.01);
        //}

        //// Place into matrix
        //for (int j = 0; j<hrPixels; j++)
        //{
        //    upsampledMatrix[j, i] = hrPc[j];
        //}

        private async void test7_Click(object sender, RoutedEventArgs e)
        {
            var fixedImage = ImageGenerator.Instance.FromFile(@"D:\Data\Control Point Test\Fixed.png");
            var movingImage = ImageGenerator.Instance.FromFile(@"D:\Data\Control Point Test\Moving.png");

            var fixedData = ImageGenerator.Instance.ConvertToData2D(fixedImage);
            var movingData = ImageGenerator.Instance.ConvertToData2D(movingImage);

            fixedData.DataName = "Fixed";
            movingData.DataName = "Moving";

            var fixedData256x256 = fixedData.Downscale(256, 256);
            var movingData256x256 = movingData.Downscale(256, 256);

            var fixedData1769x2043 = fixedData.Upscale(1769, 2043);
            var movingData1769x2043 = movingData.Upscale(1769, 2043);

            AddTables(new Data2D[] { fixedData, movingData, fixedData256x256, movingData256x256, fixedData1769x2043, movingData1769x2043 });
            return;

            var spec = AvailableHost.AvailableSpectraSource.GetAvailableSpectra()[0] as Cameca1280Spectrum;

            var masses = new List<Data2D>();
            foreach (var species in spec.Species)
            {
                masses.Add(spec.FromSpecies(species));
            }

            int lrSizeX = spec.SizeX;
            int lrSizeY = spec.SizeY;
            int lrPixels = spec.SizeX * spec.SizeY;
            int hrSizeX = lrSizeX * 2;
            int hrSizeY = lrSizeY * 2;
            int hrPixels = hrSizeX * hrSizeY;
            int numMasses = masses.Count;

            // Normalize each mass to [0,1]
            double[,] hsMatrix = new double[lrPixels, numMasses];
            for (int i = 0; i < numMasses; i++)
            {
                for (int x = 0; x < lrSizeX; x++)
                {
                    for (int y = 0; y < lrSizeY; y++)
                    {
                        hsMatrix[x * lrSizeY + y, i] = masses[i][x, y];// / masses[i].Maximum;
                    }
                }
            }

            // Mean center the columns
            double[] means = new double[numMasses];
            for (int i = 0; i < numMasses; i++)
            {
                double sum = 0;
                for (int j = 0; j < lrPixels; j++)
                {
                    sum += hsMatrix[j, i];
                }
                means[i] = sum / lrPixels;
                for (int j = 0; j < lrPixels; j++)
                {
                    hsMatrix[j, i] -= means[i];
                }
            }

            var svd = new SingularValueDecomposition(hsMatrix, true, true, false, false);
            var u = svd.LeftSingularVectors;
            var d = svd.Diagonal;

            double[,] upsampledMatrix = new double[hrPixels, numMasses];
            for (int i = 0; i < numMasses; i++)
            {
                double[,] pcReshaped = new double[lrSizeX, lrSizeY];
                for (int x = 0; x < lrSizeX; x++)
                {
                    for (int y = 0; y < lrSizeY; y++)
                    {
                        pcReshaped[x, y] = u[x * lrSizeY + y, i];
                    }
                }

                var upscaled = pcReshaped.Upscale(hrSizeX, hrSizeY, false);

                for (int x = 0; x < hrSizeX; x++)
                {
                    for (int y = 0; y < hrSizeY; y++)
                    {
                        upsampledMatrix[x * hrSizeY + y, i] = upscaled[x, y];
                    }
                }
            }

            var reverted = upsampledMatrix.Dot(svd.DiagonalMatrix).DotWithTransposed(svd.RightSingularVectors);

            for (int i = 0; i < numMasses; i++)
            {
                Data2D b = new Data2D(hrSizeX, hrSizeY);
                for (int x = 0; x < hrSizeX; x++)
                {
                    for (int y = 0; y < hrSizeY; y++)
                    {
                        b[x, y] = (float)reverted[x * hrSizeY + y, i];
                    }
                }
                AddTable(b);
            }

            //var tables = await Task.Run(async () =>
            //{
            //    List<Data2D> tablesToReturn = new List<Data2D>();

            //    var bsHighRes = ImageGenerator.Instance.FromFile(@"D:\Data\10-01-12\1a.bmp");
            //    var bsLowRes = ImageGenerator.Instance.FromFile(@"D:\Data\10-01-12\3_64.bmp");

            //    //var bsHighRes = ImageGenerator.Instance.FromFile(@"D:\Data\Al-Li Alloy\Hyperspectral\Registered_800.png");

            //    var highRes = ImageGenerator.Instance.ConvertToData2D(bsHighRes);
            //    highRes.DataName = "High Res";
            //    //var lowRes = await Data2D.LoadData2DAsync(@"D:\Data\Al-Li Alloy\Hyperspectral\28.txt", FileType.CSV);
            //    var lowRes = ImageGenerator.Instance.ConvertToData2D(bsLowRes, Data2DConverionType.Color, Color.FromArgb(255, 0, 255, 0));
            //    lowRes.DataName = "Low Res";

            //    tablesToReturn.Add(highRes);
            //    tablesToReturn.Add(lowRes);

            //    float hrMax = highRes.Maximum;
            //    float lrMax = lowRes.Maximum;

            //    // Normalize
            //    highRes /= hrMax;
            //    lowRes /= lrMax;

            //    int resolutionRatio = (int)Math.Min(
            //        Math.Ceiling((double)highRes.Width / lowRes.Width),
            //        Math.Ceiling((double)highRes.Height / lowRes.Height));

            //    int numDecompositions = (int)Math.Log(resolutionRatio, 2);

            //    Data2D[] s = new Data2D[numDecompositions + 1];
            //    Data2D[] g = new Data2D[numDecompositions + 1];
            //    Data2D[] l = new Data2D[numDecompositions + 1];

            //    for (int i = 0; i < numDecompositions + 1; i++)
            //    {
            //        s[i] = i == 0 ? highRes : s[i - 1].Downscale(g[i - 1].Width / 2, g[i - 1].Height / 2);
            //        g[i] = Filter.DoFilter(s[i], FilterType.Gauissian);
            //        l[i] = s[i] - g[i];

            //        s[i].DataName = $"S {i}";
            //        tablesToReturn.Add(s[i]);
            //        g[i].DataName = $"G {i}";
            //        tablesToReturn.Add(g[i]);
            //        l[i].DataName = $"L {i}";
            //        tablesToReturn.Add(l[i]);
            //    }

            //    Data2D[] r = new Data2D[numDecompositions + 1];
            //    for (int i = numDecompositions; i >= 0; i--)
            //    {
            //        if (i == numDecompositions)
            //        {
            //            if (lowRes.Width != l[i].Width || lowRes.Height != l[i].Height)
            //            {
            //                lowRes = lowRes.Resize(l[i].Width, l[i].Height);
            //            }
            //            r[i] = lowRes + l[i];
            //        }
            //        else
            //        {
            //            var upscaled = EmptyUpscale(r[i + 1], 2);
            //            upscaled = Filter.DoFilter(upscaled, FilterType.Gauissian) * 4f;
            //            r[i] = upscaled + l[i];
            //        }
            //        r[i].DataName = $"R {i}";
            //    }

            //    tablesToReturn.AddRange(r);

            //    // Unnormalize
            //    var fused = r[0] *= lrMax;
            //    fused += -fused.Minimum;
            //    fused.DataName = "Fused";
            //    tablesToReturn.Add(fused);

            //    return tablesToReturn;
            //});
        }
        private async void test8_Click(object sender, RoutedEventArgs e)
        {
            var fixedImage = ImageGenerator.Instance.FromFile(@"D:\Data\Control Point Test\f.bmp");
            var fixedData = ImageGenerator.Instance.ConvertToData2D(fixedImage);
            fixedData.DataName = "Fixed";

            var movingImage = ImageGenerator.Instance.FromFile(@"D:\Data\Control Point Test\m.bmp");
            var movingData = ImageGenerator.Instance.ConvertToData2D(movingImage);
            movingData.DataName = "Moving";

            AddTables(new Data2D[] { fixedData, movingData });

            var fpt = new FusionPointTab();
            fpt.ViewModel.FixedImageViewModel.DataSource = fixedData;
            fpt.ViewModel.MovingImageViewModel.DataSource = movingData;
            var cti = ClosableTabItem.Create(fpt, TabType.FusionPoint);
            AddTabItemAndNavigate(cti);
            return;

            var ft = tabMain.SelectedContent as FusionPointTab;
            await ft.TestRegistration();

            return;

            var data256x256 = new Data2D(256, 256) { DataName = "256x256" };
            var data512x256 = new Data2D(512, 256) { DataName = "512x256" };
            var data256x512 = new Data2D(256, 512) { DataName = "256x512" };
            var data512x512 = new Data2D(512, 512) { DataName = "512x512" };

            for (int x = 0; x < data256x256.Width; x++)
            {
                for (int y = 0; y < data256x256.Width; y++)
                {
                    if (x > data256x256.Width / 2 - 16 && x < data256x256.Width / 2 + 16) data256x256[x, y] = 100;
                    if (y > data256x256.Height / 2 - 16 && y < data256x256.Height / 2 + 16) data256x256[x, y] = 100;

                    if (IsInCircle(x, y, 10, 10, 5)) data256x256[x, y] = 100;
                    if (IsInCircle(x, y, 10, 246, 5)) data256x256[x, y] = 100;
                    if (IsInCircle(x, y, 246, 10, 5)) data256x256[x, y] = 100;
                    if (IsInCircle(x, y, 246, 246, 5)) data256x256[x, y] = 100;
                }
            }

            for (int x = 0; x < data512x256.Width; x++)
            {
                for (int y = 0; y < data512x256.Height; y++)
                {
                    if (x > data512x256.Width / 2 - 32 && x < data512x256.Width / 2 + 32) data512x256[x, y] = 100;
                    if (y > data512x256.Height / 2 - 16 && y < data512x256.Height / 2 + 16) data512x256[x, y] = 100;

                    if (IsInCircle(x, y, 20, 10, 5)) data512x256[x, y] = 100;
                    if (IsInCircle(x, y, 20, 246, 5)) data512x256[x, y] = 100;
                    if (IsInCircle(x, y, 492, 10, 5)) data512x256[x, y] = 100;
                    if (IsInCircle(x, y, 492, 246, 5)) data512x256[x, y] = 100;
                }
            }

            for (int x = 0; x < data256x512.Width; x++)
            {
                for (int y = 0; y < data256x512.Height; y++)
                {
                    if (x > data256x512.Width / 2 - 16 && x < data256x512.Width / 2 + 16) data256x512[x, y] = 100;
                    if (y > data256x512.Height / 2 - 32 && y < data256x512.Height / 2 + 32) data256x512[x, y] = 100;

                    if (IsInCircle(x, y, 10, 20, 5)) data256x512[x, y] = 100;
                    if (IsInCircle(x, y, 10, 492, 5)) data256x512[x, y] = 100;
                    if (IsInCircle(x, y, 246, 20, 5)) data256x512[x, y] = 100;
                    if (IsInCircle(x, y, 246, 492, 5)) data256x512[x, y] = 100;
                }
            }

            for (int x = 0; x < data512x512.Width; x++)
            {
                for (int y = 0; y < data512x512.Height; y++)
                {
                    if (x > data512x512.Width / 2 - 32 && x < data512x512.Width / 2 + 32) data512x512[x, y] = 100;
                    if (y > data512x512.Height / 2 - 32 && y < data512x512.Height / 2 + 32) data512x512[x, y] = 100;

                    if (IsInCircle(x, y, 20, 20, 5)) data512x512[x, y] = 100;
                    if (IsInCircle(x, y, 20, 492, 5)) data512x512[x, y] = 100;
                    if (IsInCircle(x, y, 492, 20, 5)) data512x512[x, y] = 100;
                    if (IsInCircle(x, y, 492, 492, 5)) data512x512[x, y] = 100;
                }
            }

            AddTables(new Data2D[] { data256x256, data512x256, data256x512, data512x512 });

            var dt = new DataDisplayTab(ColorScaleTypes.ThermalCold);
            foreach (var table in GetAvailableTables())
            {
                dt.AddDataSource(table);
            }
            AddTabItemAndNavigate(ClosableTabItem.Create(dt, TabType.DataDisplay));
        }
        private bool IsInCircle(int x, int y, int cx, int cy, int radius)
        {
            return ((x - cx) * (x - cx) + (y - cy) * (y - cy)) <= radius * radius;
        }

        private async void test9_Click(object sender, RoutedEventArgs e)
        {
            //var dt = new DataDisplayTab(ColorScaleTypes.ThermalCold);
            //var cti = ClosableTabItem.Create(dt, TabType.DataDisplay);
            //AddTabItemAndNavigate(cti);

            //var selected = GetSelectedTables();

            //selected[0].FlipHorizontal();
            //selected[1].FlipVertical();
            //selected[2].FlipDiagonal(false);
            //selected[3].FlipDiagonal(true);

            //await dt.AddDataSourceAsync(selected);

            HwndSource hwnd = new HwndSource(0, 0x12cf0000, 0, 50, 50, 250, 250, "Test Window", IntPtr.Zero);
            hwnd.RootVisual = new System.Windows.Shapes.Rectangle() { Fill = Brushes.GreenYellow, Width = 100, Height = 100 };
        }
        private async void test10_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "Raw binary files (.raw)|*.raw";
            if (ofd.ShowDialog() != true) return;

            Data3D orignal = new Data3D(768, 256, 500);

            using (Stream stream = File.OpenRead(ofd.FileName))
            {
                BinaryReader br = new BinaryReader(stream);

                for (int x = 0; x < orignal.Width; x++)
                {
                    for (int y = 0; y < orignal.Height; y++)
                    {
                        for (int z = 0; z < orignal.Depth; z++)
                        {
                            orignal[x, y, z] = br.ReadSingle();
                        }
                    }
                }
            }

            int binSize = 5;

            Data3D binned = new Data3D(orignal.Width, orignal.Height, orignal.Depth / binSize);

            for (int x = 0; x < binned.Width; x++)
            {
                for (int y = 0; y < binned.Height; y++)
                {
                    for (int z = 0; z < binned.Depth; z++)
                    {
                        float sum = 0;
                        for (int w = 0; w < binSize; w++)
                        {
                            sum += orignal[x, y, z * binSize + w];
                        }
                        binned[x, y, z] = sum;
                    }
                }
            }

            string binnedFilePath = ofd.FileName.Insert(ofd.FileName.Length - 4, "_binned");

            using (Stream stream = File.OpenWrite(binnedFilePath))
            {
                BinaryWriter bw = new BinaryWriter(stream);

                for (int x = 0; x < binned.Width; x++)
                {
                    for (int y = 0; y < binned.Height; y++)
                    {
                        for (int z = 0; z < binned.Depth; z++)
                        {
                            bw.Write(binned[x, y, z]);
                        }
                    }
                }
            }
        }
#pragma warning restore 1998

        #endregion

        #region Input Events
        private void Grid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F8)
            {
                ClosableTabItem cti;

                foreach (object tab in tabMain.Items)
                {
                    cti = tab as ClosableTabItem;
                    if (cti == null) continue;

                    if (cti.TabType == TabType.Settings)
                    {
                        tabMain.SelectedItem = tab;
                        return;
                    }
                }

                cti = ClosableTabItem.Create(new SettingsTab(Workspace.Registry), TabType.Settings);
                tabMain.Items.Add(cti);
                tabMain.SelectedItem = cti;
            }
        }
        #endregion

        #region Code Editor
        private void ribbonExecuteCode_Click(object sender, RoutedEventArgs e)
        {
            System.CodeDom.Compiler.CompilerResults results = externalCodeEditor.CompileCode();
        }
        #endregion

        #region IAvailableTables
        public List<Data2D> GetSelectedTables()
        {
            List<Data2D> tables = new List<Data2D>();
            foreach (object obj in listViewData.SelectedItems)
            {
                Data2D d = obj as Data2D;
                if (d != null)
                    tables.Add(d);
            }
            return tables;
        }
        public List<Data2D> GetAvailableTables()
        {
            return Workspace.Data.ToList();
        }
        public void RemoveTables(IEnumerable<Data2D> tablesToRemove)
        {
            List<Data2D> notRemoved = new List<Data2D>();
            
            foreach(Data2D d in tablesToRemove)
            {
                if (Workspace.Data.Contains(d))
                {
                    try
                    {
                        Workspace.Data.Remove(d);
                    }
                    catch (Exception)
                    {
                        notRemoved.Add(d);
                    }
                }
                else notRemoved.Add(d);
            }

            if(notRemoved.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                foreach(Data2D d in notRemoved)
                {
                    sb.AppendLine(d.DataName);
                }

                DialogBox.Show("The following tables could not be removed from the workspace:",
                    sb.ToString(), "Remove", DialogIcon.Alert);
            }
        }
        public void RemoveTables(Data2D[] tablesToRemove)
        {
            List<Data2D> notRemoved = new List<Data2D>();

            foreach (Data2D d in tablesToRemove)
            {
                if (Workspace.Data.Contains(d))
                {
                    try
                    {
                        Workspace.Data.Remove(d);
                    }
                    catch (Exception)
                    {
                        notRemoved.Add(d);
                    }
                }
                else notRemoved.Add(d);
            }

            if (notRemoved.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                foreach (Data2D d in notRemoved)
                {
                    sb.AppendLine(d.DataName);
                }

                DialogBox.Show("The following tables could not be removed from the workspace:",
                    sb.ToString(), "Remove", DialogIcon.Alert);
            }
        }
        public void AddTable(Data2D tableToAdd)
        {
            try
            {
                Workspace.Data.Add(tableToAdd);
            }
            catch (Exception ex)
            {
                DialogBox.Show("The table could not be added to the workspace.",
                    ex.Message, "Add", DialogIcon.Alert);
                return;
            }
        }
        public void AddTables(IEnumerable<Data2D> tablesToAdd)
        {
            List<Data2D> notAdded = new List<Data2D>();

            foreach(Data2D d in tablesToAdd)
            {
                try
                {
                    Workspace.Data.Add(d);
                }
                catch (Exception)
                {
                    notAdded.Add(d);
                }
            }

            if (notAdded.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                foreach (Data2D d in notAdded)
                {
                    sb.AppendLine(d.DataName);
                }

                DialogBox.Show("The following tables could not be added to the workspace:",
                    sb.ToString(), "Add", DialogIcon.Alert);
            }
        }
        public void AddTables(params Data2D[] tablesToAdd)
        {
            List<Data2D> notAdded = new List<Data2D>();

            foreach (Data2D d in tablesToAdd)
            {
                try
                {
                    Workspace.Data.Add(d);
                }
                catch (Exception)
                {
                    notAdded.Add(d);
                }
            }

            if (notAdded.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                foreach (Data2D d in notAdded)
                {
                    sb.AppendLine(d.DataName);
                }

                DialogBox.Show("The following tables could not be added to the workspace:",
                    sb.ToString(), "Add", DialogIcon.Alert);
            }
        }
        public void ReplaceTable(Data2D tableToReplace, Data2D newTable)
        {
            bool isSelected = false;

            try
            {
                if (Workspace.Data.Contains(tableToReplace))
                {                    
                    foreach(Data2D d in GetSelectedTables())
                    {
                        if (d == tableToReplace)
                            isSelected = true;
                        break;
                    }

                    int index = Workspace.Data.IndexOf(tableToReplace);
                    Workspace.Data.Remove(tableToReplace);
                    Workspace.Data.Insert(index, newTable);
                }
            }
            catch (Exception ex)
            {
                DialogBox.Show("Could not add the new table to the collection.", ex.Message,
                         "Replace", DialogIcon.Error);
                return;
            }

            if (isSelected)
            {
                try
                {
                    listViewData.SelectedItems.Add(newTable);
                }
                catch(Exception ex)
                {
                    DialogBox.Show("Could not restore the state of the list. Don't worry though, the table replacement did function properly.",
                        ex.Message, "Replace", DialogIcon.Alert);
                    return;
                }
            }
        }
        public void SelectTable(Data2D toSelect, bool clearSelected = false)
        {
            if (clearSelected)
            {
                listViewData.SelectedItems.Clear();
            }
            try
            {
                listViewData.SelectedItems.Add(toSelect);
            }
            catch(Exception ex)
            {
                DialogBox.Show("Could not select the specified table(s).",
                    ex.Message, "Select", DialogIcon.Error);
                return;
            }
        }
        public void SelectTables(IEnumerable<Data2D> toSelect, bool clearSelected = false)
        {
            if (clearSelected)
            {
                listViewData.SelectedItems.Clear();
            }
            try
            {
                foreach (Data2D d in toSelect)
                {
                    listViewData.SelectedItems.Add(d);
                }
            }
            catch (Exception ex)
            {
                DialogBox.Show("Could not select the specified table(s).",
                    ex.Message, "Select", DialogIcon.Error);
                return;
            }
        }
        public void SelectTables(Data2D[] toSelect, bool clearSelected = false)
        {
            if (clearSelected)
            {
                listViewData.SelectedItems.Clear();
            }
            try
            {
                foreach (Data2D d in toSelect)
                {
                    listViewData.SelectedItems.Add(d);
                }
            }
            catch (Exception ex)
            {
                DialogBox.Show("Could not select the specified table(s).",
                    ex.Message, "Select", DialogIcon.Error);
                return;
            }
        }
        #endregion

        #region IAvailableImageSeries
        public List<DisplaySeries> GetSelectedImageSeries()
        {
            List<DisplaySeries> series = new List<DisplaySeries>();
            foreach(object obj in listViewImageSeries.SelectedItems)
            {
                DisplaySeries d = obj as DisplaySeries;
                if (d != null)
                    series.Add(d);
            }
            return series;
        }
        public List<DisplaySeries> GetAvailableImageSeries()
        {
            return Workspace.ImageSeries.ToList();
        }
        #endregion

        #region IAvailableVolumes
        public List<Volume> GetSelectedVolumes()
        {
            List<Volume> volumes = new List<Volume>();
            foreach (object obj in listViewVolumes.SelectedItems)
            {
                Volume v = obj as Volume;
                if (v != null)
                    volumes.Add(v);
            }
            return volumes;
        }
        public List<Volume> GetAvailableVolumes()
        {
            return Workspace.Volumes.ToList();
        }
        public void RemoveVolumes(IEnumerable<Volume> volumesToRemove)
        {
            List<Volume> notRemoved = new List<Volume>();

            foreach (Volume v in volumesToRemove)
            {
                if (Workspace.Volumes.Contains(v))
                {
                    try
                    {
                        Workspace.Volumes.Remove(v);
                    }
                    catch (Exception)
                    {
                        notRemoved.Add(v);
                    }
                }
                else notRemoved.Add(v);
            }

            if (notRemoved.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                foreach (Volume v in notRemoved)
                {
                    sb.AppendLine(v.VolumeName);
                }

                DialogBox.Show("The following volumes could not be removed from the workspace:",
                    sb.ToString(), "Remove", DialogIcon.Alert);
            }
        }
        public void RemoveVolumes(Volume[] volumesToRemove)
        {
            List<Volume> notRemoved = new List<Volume>();

            foreach (Volume v in volumesToRemove)
            {
                if (Workspace.Volumes.Contains(v))
                {
                    try
                    {
                        Workspace.Volumes.Remove(v);
                    }
                    catch (Exception)
                    {
                        notRemoved.Add(v);
                    }
                }
                else notRemoved.Add(v);
            }

            if (notRemoved.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                foreach (Volume v in notRemoved)
                {
                    sb.AppendLine(v.VolumeName);
                }

                DialogBox.Show("The following volumes could not be removed from the workspace:",
                    sb.ToString(), "Remove", DialogIcon.Alert);
            }
        }
        public void AddVolumes(IEnumerable<Volume> volumesToAdd)
        {
            List<Volume> notAdded = new List<Volume>();

            foreach (Volume v in volumesToAdd)
            {
                try
                {
                    Workspace.Volumes.Add(v);
                }
                catch (Exception)
                {
                    notAdded.Add(v);
                }
            }

            if(notAdded.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                foreach (Volume v in notAdded)
                {
                    sb.AppendLine(v.VolumeName);
                }

                DialogBox.Show("The following volumes could not be added to the workspace:",
                    sb.ToString(), "Add", DialogIcon.Alert);
            }
        }
        public void AddVolumes(Volume[] volumesToAdd)
        {
            List<Volume> notAdded = new List<Volume>();

            foreach (Volume v in volumesToAdd)
            {
                try
                {
                    Workspace.Volumes.Add(v);
                }
                catch (Exception)
                {
                    notAdded.Add(v);
                }
            }

            if (notAdded.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                foreach (Volume v in notAdded)
                {
                    sb.AppendLine(v.VolumeName);
                }

                DialogBox.Show("The following volumes could not be added to the workspace:",
                    sb.ToString(), "Add", DialogIcon.Alert);
            }
        }
        public void ReplaceVolume(Volume toReplace, Volume newvolume)
        {
            bool isSelected = false;

            try
            {
                if (Workspace.Volumes.Contains(newvolume))
                {
                    foreach (Volume v in GetSelectedVolumes())
                    {
                        if (v == newvolume)
                            isSelected = true;
                        break;
                    }

                    int index = Workspace.Volumes.IndexOf(toReplace);
                    Workspace.Volumes.Remove(toReplace);
                    Workspace.Volumes.Insert(index, newvolume);
                }
            }
            catch (Exception ex)
            {
                DialogBox.Show("Could not add the new volume to the collection.", ex.Message,
                         "Replace", DialogIcon.Error);
                return;
            }

            if (isSelected)
            {
                try
                {
                    listViewVolumes.SelectedItems.Add(newvolume);
                }
                catch (Exception ex)
                {
                    DialogBox.Show("Could not restore the state of the list. Don't worry though, the table replacement did function properly.",
                        ex.Message, "Replace", DialogIcon.Alert);
                    return;
                }
            }
        }
        #endregion

        #region IAvailableSpectra
        public List<Spectrum> GetSelectedSpectra()
        {
            List<Spectrum> spectra = new List<Spectrum>();
            foreach (object obj in listViewSpectra.SelectedItems)
            {
                Spectrum s = obj as Spectrum;
                if (s != null)
                    spectra.Add(s);
            }
            return spectra;
        }
        public List<Spectrum> GetAvailableSpectra()
        {
            return Workspace.Spectra.ToList();
        }
        public void RemoveSpectra(IEnumerable<Spectrum> spectraToRemove)
        {
            List<Spectrum> notRemoved = new List<Spectrum>();

            foreach (Spectrum s in spectraToRemove)
            {
                if (Workspace.Spectra.Contains(s))
                {
                    try
                    {
                        Workspace.Spectra.Remove(s);
                    }
                    catch (Exception)
                    {
                        notRemoved.Add(s);
                    }
                }
                else notRemoved.Add(s);
            }

            if (notRemoved.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                foreach (Spectrum s in notRemoved)
                {
                    sb.AppendLine(s.Name);
                }

                DialogBox.Show("The following spectra could not be removed from the workspace:",
                    sb.ToString(), "Remove", DialogIcon.Alert);
            }
        }
        public void RemoveSpectra(Spectrum[] spectraToRemove)
        {
            List<Spectrum> notRemoved = new List<Spectrum>();

            foreach (Spectrum s in spectraToRemove)
            {
                if (Workspace.Spectra.Contains(s))
                {
                    try
                    {
                        Workspace.Spectra.Remove(s);
                    }
                    catch (Exception)
                    {
                        notRemoved.Add(s);
                    }
                }
                else notRemoved.Add(s);
            }

            if (notRemoved.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                foreach (Spectrum s in notRemoved)
                {
                    sb.AppendLine(s.Name);
                }

                DialogBox.Show("The following spectra could not be removed from the workspace:",
                    sb.ToString(), "Remove", DialogIcon.Alert);
            }
        }
        public void AddSpectrum(Spectrum spectrumToAdd)
        {
            try
            {
                Workspace.Spectra.Add(spectrumToAdd);
            }
            catch (Exception ex)
            {
                DialogBox.Show("The spectrum could not be added to the workspace.",
                    ex.Message, "Add", DialogIcon.Alert);
            }
        }
        public void AddSpectra(IEnumerable<Spectrum> spectraToAdd)
        {
            List<Spectrum> notAdded = new List<Spectrum>();

            foreach (Spectrum s in spectraToAdd)
            {
                try
                {
                    Workspace.Spectra.Add(s);
                }
                catch (Exception)
                {
                    notAdded.Add(s);
                }
            }

            if (notAdded.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                foreach (Spectrum s in notAdded)
                {
                    sb.AppendLine(s.Name);
                }

                DialogBox.Show("The following spectra could not be added to the workspace:",
                    sb.ToString(), "Add", DialogIcon.Alert);
            }
        }
        public void AddSpectra(Spectrum[] spectraToAdd)
        {
            List<Spectrum> notAdded = new List<Spectrum>();

            foreach (Spectrum s in spectraToAdd)
            {
                try
                {
                    Workspace.Spectra.Add(s);
                }
                catch (Exception)
                {
                    notAdded.Add(s);
                }
            }

            if (notAdded.Count > 0)
            {
                StringBuilder sb = new StringBuilder();
                foreach (Spectrum s in notAdded)
                {
                    sb.AppendLine(s.Name);
                }

                DialogBox.Show("The following spectra could not be added to the workspace:",
                    sb.ToString(), "Add", DialogIcon.Alert);
            }
        }
        #endregion

        #region TabControl
        private void AddTabItem(ClosableTabItem tabItem)
        {
            tabMain.Items.Add(tabItem);
        }
        private void AddTabItemAndNavigate(ClosableTabItem tabItem)
        {
            tabMain.Items.Add(tabItem);
            tabMain.SelectedItem = tabItem;
        }
        #endregion
    }
}