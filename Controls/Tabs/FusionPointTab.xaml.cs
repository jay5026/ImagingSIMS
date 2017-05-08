﻿using ImagingSIMS.Controls.ViewModels;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ImagingSIMS.Common.Dialogs;
using ImagingSIMS.Data;
using ImagingSIMS.Data.Imaging;
using ImagingSIMS.Data.Fusion;
using Microsoft.Win32;
using ImagingSIMS.Common;

namespace ImagingSIMS.Controls.Tabs
{
    /// <summary>
    /// Interaction logic for FusionRegistrationTab.xaml
    /// </summary>
    public partial class FusionPointTab : UserControl, IDroppableTab
    {
        public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register("ViewModel",
            typeof(FusionPointTabViewModel), typeof(FusionPointTab));

        public FusionPointTabViewModel ViewModel
        {
            get { return (FusionPointTabViewModel)GetValue(ViewModelProperty); }
            set { SetValue(ViewModelProperty, value); }
        }

        public FusionPointTab()
        {
            ViewModel = new FusionPointTabViewModel();

            InitializeComponent();
        }        
        private void Register_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = !ViewModel.IsRegistering;
        }
        private void CancelRegistration_CanExeucte(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = ViewModel.IsRegistering;
        }
        private async void Register_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var fixedPoints = ViewModel.FixedImageViewModel.ControlPoints.Select(p => new Point(p.CoordX, p.CoordY));
            var movingPoints = ViewModel.MovingImageViewModel.ControlPoints.Select(p => new Point(p.CoordX, p.CoordY));

            if(fixedPoints.Count() < 4 || movingPoints.Count() < 4)
            {
                DialogBox.Show("Not enough control points selected.",
                    "At least 4 points in each image need to be specified for registration.", "Registration", DialogIcon.Error);
                return;
            }
            if(fixedPoints.Count() != movingPoints.Count())
            {
                DialogBox.Show("Unequal number of control points selected.",
                    "Select the same number of control points in each image and try again.", "Registration", DialogIcon.Error);
                return;
            }

            var registered = await PointRegistration.RegisterAsync(ViewModel.FixedImageViewModel.DataSource,
                ViewModel.MovingImageViewModel.DataSource, fixedPoints.Convert(), movingPoints.Convert());

            ViewModel.MovingImageViewModel.ChangeDataSource(registered);

            if (ViewModel.FixedImageViewModel.ClearPointsOnRegistration)
                ViewModel.FixedImageViewModel.ControlPoints.Clear();
            if (ViewModel.MovingImageViewModel.ClearPointsOnRegistration)
                ViewModel.MovingImageViewModel.ControlPoints.Clear();

            ViewModel.RegisteredOverlay = Overlay.CreateFalseColorOverlay(ViewModel.FixedImageViewModel.DisplayImage,
                ViewModel.MovingImageViewModel.DisplayImage, FalseColorOverlayMode.GreenMagenta);
        }

        private void Fuse_CanExeucte(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = ViewModel.FixedImageViewModel.DisplayImage != null 
                && ViewModel.MovingImageViewModel.DisplayImage != null;
        }
        private async void Fuse_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (ViewModel.FixedImageViewModel.DisplayImage == null)
            {
                DialogBox db = new DialogBox("No high resolution image loaded.",
                    "Drop or load a high resolution image into the tab and try again.", "Fusion", DialogIcon.Error);
                db.ShowDialog();
                return;
            }
            if (ViewModel.MovingImageViewModel.DisplayImage == null)
            {
                DialogBox db = new DialogBox("No low resolution image loaded.",
                    "Drop or load a low resolution image into the tab and try again.", "Fusion", DialogIcon.Error);
                db.ShowDialog();
                return;
            }
            if (ViewModel.FixedImageViewModel.DisplayImage.PixelWidth < ViewModel.MovingImageViewModel.DisplayImage.PixelWidth ||
                ViewModel.FixedImageViewModel.DisplayImage.PixelHeight < ViewModel.MovingImageViewModel.DisplayImage.PixelHeight)
            {
                DialogBox db = new DialogBox("Invalid image size.",
                    "One or both of the dimensions of the high resolution image is(are) smaller than the low resolution image.", "Fusion", DialogIcon.Error);
                db.ShowDialog();
                return;
            }

            var fusionType = ViewModel.FusionType;
            var conversionType = ViewModel.PanchromaticConversion;

            Data2D highRes = ImageGenerator.Instance.ConvertToData2D(ViewModel.FixedImageViewModel.DisplayImage, 
                ViewModel.PanchromaticConversion, ViewModel.PanchromaticConversionSolidColor);
            Data3D lowRes = ImageGenerator.Instance.ConvertToData3D(ViewModel.MovingImageViewModel.DisplayImage);

            if (fusionType == FusionType.HSLShift && ViewModel.ShiftWindowSize <= 0)
            {
                DialogBox.Show("Invalid window size.", "Window size must be greater than 0.",
                    "Fusion", DialogIcon.Error);
                return;
            }

            Fusion fusion;

            switch (fusionType)
            {
                case FusionType.HSL:
                    fusion = new HSLFusion(highRes, lowRes);
                    break;
                case FusionType.WeightedAverage:
                    fusion = new WeightedAverageFusion(highRes, lowRes);
                    break;
                case FusionType.HSLSmooth:
                    fusion = new HSLSmoothFusion(highRes, lowRes);
                    break;
                case FusionType.Adaptive:
                    fusion = new AdaptiveIHSFusion(highRes, lowRes);
                    break;
                case FusionType.HSLShift:
                    fusion = new HSLShiftFusion(highRes, lowRes);
                    ((HSLShiftFusion)fusion).WindowSize = ViewModel.ShiftWindowSize;
                    break;
                case FusionType.PCA:
                    fusion = new PCAFusion(highRes, lowRes);
                    break;
                case FusionType.IHS:
                    fusion = new IHSFusion(highRes, lowRes);
                    break;
                default:
                    Mouse.OverrideCursor = Cursors.Arrow;
                    return;
            }

            try
            {
                if (fusion is Pansharpening)
                    ((Pansharpening)fusion).CheckFusion();

                Mouse.OverrideCursor = Cursors.Wait;

                Data3D result = await fusion.DoFusionAsync();
                ViewModel.FusedImage = ImageGenerator.Instance.Create(result);

                ViewModel.AnalysisResults = "";

                if (fusion is HSLShiftFusion)
                {
                    if (((HSLShiftFusion)fusion).ShiftCalculationCompleted)
                    {
                        Point shift = ((HSLShiftFusion)fusion).ShiftSize;
                        Point newCenter = ((HSLShiftFusion)fusion).NewCenter;
                        double distance = ((HSLShiftFusion)fusion).DistanceToCenter;

                        textBoxCC.Text = string.Format("Shift results:\nShift size: ({0},{1})\nNew center: ({2},{3})\nDistance to center: {4}",
                            shift.X, shift.Y, newCenter.X, newCenter.Y, distance);
                        tabControlOutputs.SelectedIndex = 1;
                    }
                }

                updateClosableTabItem("Fusion complete.");
            }
            catch (Exception ex)
            {
                DialogBox db = new DialogBox("There was a problem fusing the two images.", ex.Message, "Fusion", DialogIcon.Error);
                db.ShowDialog();
                return;
            }
            finally
            {
                Mouse.OverrideCursor = Cursors.Arrow;
            }
        }

        private void Save_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = ViewModel.FusedImage != null;
        }
        private void Copy_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = ViewModel.FusedImage != null;
        }

        private void Save_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (ViewModel.FusedImage == null) return;

            var sfd = new SaveFileDialog();
            sfd.Filter = "Bitmap Images (.bmp)|*.bmp";
            if (sfd.ShowDialog() != true) return;

            ViewModel.FusedImage.Save(sfd.FileName);

            DialogBox.Show("Image saved successfully!", sfd.FileName, "Save", DialogIcon.Ok);
        }
        private void Copy_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (ViewModel.FusedImage == null) return;

            Clipboard.SetImage(ViewModel.FusedImage);
        }

        public void HandleDragDrop(object sender, DragEventArgs e)
        {
            bool isValidDataFormat = e.Data.GetDataPresent(DataFormats.Bitmap) ||
                e.Data.GetDataPresent("DisplayImage") || e.Data.GetDataPresent("Data2D");

            if (!isValidDataFormat) return;

            var dropDialog = new FusionDropBox();
            if (dropDialog.ShowDialog() != true) return;

            bool isHighRes = dropDialog.DropResult == FusionDropResult.SEM;

            Data2D data = null;

            if (e.Data.GetDataPresent(DataFormats.Bitmap))
            {
                var bs = e.Data.GetData(DataFormats.Bitmap) as BitmapSource;
                if(bs != null)
                {
                    data = ImageGenerator.Instance.ConvertToData2D(bs, Data2DConverionType.Grayscale);
                }
            }
            else if (e.Data.GetDataPresent("DisplayImage"))
            {
                var bs = (e.Data.GetData("DisplayImage") as DisplayImage)?.Source as BitmapSource;
                if (bs != null)
                {
                    data = ImageGenerator.Instance.ConvertToData2D(bs, Data2DConverionType.Grayscale);
                }

            }
            else if (e.Data.GetDataPresent("Data2D"))
            {
                data = e.Data.GetData("Data2D") as Data2D;
            }

            if (data == null) return;

            if (isHighRes)
            {
                ViewModel.FixedImageViewModel.ChangeDataSource(data);
            }
            else ViewModel.MovingImageViewModel.ChangeDataSource(data);

            e.Handled = true;
        }

        private void updateClosableTabItem(string message)
        {
            if (ClosableTabItem.IsClosableTabItemHosted(this))
            {
                ClosableTabItem.SendStatusUpdate(this, message);
            }
        }
        public void SetImage(BitmapSource bitmapSource, bool isHighRes)
        {
            var data = ImageGenerator.Instance.ConvertToData2D(bitmapSource);

            SetImage(data, isHighRes);
        }
        public void SetImage(Data2D data, bool isHighRes)
        {
            if (isHighRes)
            {
                ViewModel.FixedImageViewModel.ChangeDataSource(data);
            }
            else ViewModel.MovingImageViewModel.ChangeDataSource(data);
        }
        public void SaveImage(FusionSaveParameter param)
        {
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "Bitmap Images (.bmp)|*.bmp";
            if (sfd.ShowDialog() != true) return;

            switch (param)
            {
                case FusionSaveParameter.HighRes:
                    ViewModel.FixedImageViewModel.DisplayImage.Save(sfd.FileName);
                    break;
                case FusionSaveParameter.LowRes:
                    ViewModel.MovingImageViewModel.DisplayImage.Save(sfd.FileName);
                    break;
                case FusionSaveParameter.Fused:
                    ViewModel.FusedImage.Save(sfd.FileName);
                    break;
            }

            DialogBox.Show("Image saved successfully!", sfd.FileName, "Save", DialogIcon.Ok);
        }
        public void SaveSeries()
        {
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "Bitmap Images (.bmp)|*.bmp";
            if (sfd.ShowDialog() != true) return;

            string baseFileName = sfd.FileName;

            ViewModel.FixedImageViewModel.DisplayImage.Save(
                sfd.FileName.Insert(sfd.FileName.Length - 4, "-High"));
            ViewModel.MovingImageViewModel.DisplayImage.Save(
                sfd.FileName.Insert(sfd.FileName.Length - 4, "-Low"));
            ViewModel.FusedImage.Save(
                sfd.FileName.Insert(sfd.FileName.Length - 4, "-Fused"));

            Dialog.Show("Image series saved successfully!", sfd.FileName, "Save", DialogIcon.Ok);
        }
    }    
}