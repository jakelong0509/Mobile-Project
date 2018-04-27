using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.Media.Capture;
using Windows.ApplicationModel;
using System.Threading.Tasks;
using Windows.System.Display;
using Windows.Graphics.Display;
using Windows.UI.Core;
using Windows.Media.MediaProperties;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Graphics.Imaging;
using Windows.Storage.FileProperties;
using Microsoft.ProjectOxford.Face;
using Microsoft.ProjectOxford.Face.Contract;


// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace camera
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private readonly IFaceServiceClient faceServiceClient =
            new FaceServiceClient("e0bf33377adb45518c57ff48ce252cb3", "https://westcentralus.api.cognitive.microsoft.com/face/v1.0");

        int count = 1;
        MediaCapture mediaCapture;
        bool isPreviewing;
        DisplayRequest displayRequest = new DisplayRequest();
        public MainPage()
        {
            this.InitializeComponent();
        }
        private async Task StartPreviewAsync()
        {
            try
            {
                mediaCapture = new MediaCapture();
                await mediaCapture.InitializeAsync();

                displayRequest.RequestActive();
                DisplayInformation.AutoRotationPreferences = DisplayOrientations.Landscape;

            }
            catch (UnauthorizedAccessException)
            {
                return;
            }

            try
            {
                PreviewControl.Source = mediaCapture;
                await mediaCapture.StartPreviewAsync();
                isPreviewing = true;
            }
            catch (System.IO.FileLoadException)
            {
                mediaCapture.CaptureDeviceExclusiveControlStatusChanged += _mediaCapture_CaptureDeviceExclusiveControlStatusChanged;
            }


        }

        private async void _mediaCapture_CaptureDeviceExclusiveControlStatusChanged(MediaCapture sender, MediaCaptureDeviceExclusiveControlStatusChangedEventArgs args)
        {
            if (args.Status == MediaCaptureDeviceExclusiveControlStatus.SharedReadOnlyAvailable)
            {
                return;
            }
            else if (args.Status == MediaCaptureDeviceExclusiveControlStatus.ExclusiveControlAvailable && !isPreviewing)
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
                {
                    await StartPreviewAsync();
                });
            }
        }

        private async Task CleanupCameraAsync()
        {
            if (mediaCapture != null)
            {
                if (isPreviewing)
                {
                    await mediaCapture.StopPreviewAsync();
                }

                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => {
                    PreviewControl.Source = null;
                    if (displayRequest != null)
                    {
                        displayRequest.RequestRelease();
                    }

                    mediaCapture.Dispose();
                    mediaCapture = null;

                });
            }
        }

        protected async override void OnNavigatedFrom(NavigationEventArgs e)
        {
            await CleanupCameraAsync();
        }

        private async void Application_Suspending(object sender, SuspendingEventArgs e)
        {
            // Handle global application events only if this page is active
            if (Frame.CurrentSourcePageType == typeof(MainPage))
            {
                var deferral = e.SuspendingOperation.GetDeferral();
                await CleanupCameraAsync();
                deferral.Complete();
            }
        }

        private async void StartPreview(object sender, RoutedEventArgs e)
        {
            await StartPreviewAsync();
        }

        private async void TakePicture(object sender, RoutedEventArgs e)
        {
            var lowLagCapture = await mediaCapture.PrepareLowLagPhotoCaptureAsync(ImageEncodingProperties.CreateUncompressed(MediaPixelFormat.Bgra8));
            var capturedPhoto = await lowLagCapture.CaptureAsync();
            var softwareBitmap = capturedPhoto.Frame.SoftwareBitmap;

            await lowLagCapture.FinishAsync();

            var myPictures = await Windows.Storage.StorageLibrary.GetLibraryAsync(Windows.Storage.KnownLibraryId.Pictures);
            StorageFile file = await myPictures.SaveFolder.CreateFileAsync(userName.Text.Replace(" ", "")+count.ToString()+".jpg", CreationCollisionOption.GenerateUniqueName);
            count += 1;
            using (var captureStream = new InMemoryRandomAccessStream())
            {
                await mediaCapture.CapturePhotoToStreamAsync(ImageEncodingProperties.CreateJpeg(), captureStream);

                using (var fileStream = await file.OpenAsync(FileAccessMode.ReadWrite))
                {
                    var decoder = await BitmapDecoder.CreateAsync(captureStream);
                    var encoder = await BitmapEncoder.CreateForTranscodingAsync(fileStream, decoder);

                    var properties = new BitmapPropertySet {
                        {"Sytem.Photo.Orientation", new BitmapTypedValue(PhotoOrientation.Normal, PropertyType.UInt16) }
                    };

                    try
                    {
                        await encoder.BitmapProperties.SetPropertiesAsync(properties);
                    }
                    catch (Exception err) {
                        switch (err.HResult)
                        {
                            case unchecked((int)0x88982F41): // WINCODEC_ERR_PROPERTYNOTSUPPORTED
                                                             // The file format does not support this property.
                                break;
                            default:
                                throw err;
                        }
                    }
                    await encoder.FlushAsync();
                }
            }
        }


        private async void Training(object sender, RoutedEventArgs e)
        {
            string Name = userName.Text.Replace(" ", "");
            string personGroupID = (Name+"users").ToLower();
            await faceServiceClient.CreatePersonGroupAsync(personGroupID, "app users");
            
            CreatePersonResult user = await faceServiceClient.CreatePersonAsync(
                personGroupID,
                Name
            );
            Stream s = null;
            // Change this to your pictures path
            const string userImageDir = @"C:\Users\pevip\OneDrive\Pictures";
            foreach (string imagePath in Directory.GetFiles(userImageDir, "*.jpg"))
            {
                
                await Task.Run(() => { s = File.OpenRead(imagePath); });
                await faceServiceClient.AddPersonFaceAsync(
                personGroupID, user.PersonId, s);
                  
                
            }

            await faceServiceClient.TrainPersonGroupAsync(personGroupID);

            TrainingStatus trainingStatus = null;
            while (true)
            {
                trainingStatus = await faceServiceClient.GetPersonGroupTrainingStatusAsync(personGroupID);

                if (trainingStatus.Status.ToString() != "Running")
                {
                    break;
                }

                await Task.Delay(1000);
            }

            this.Frame.Navigate(typeof(UserPage));

            //string testImageFile = @"C:\Users\pevip\OneDrive\Pictures\Test.jpg";

            //await Task.Run(() => { s = File.OpenRead(testImageFile); });
            //var faces = await faceServiceClient.DetectAsync(s);
            //var faceIds = faces.Select(face => face.FaceId).ToArray();

            //var results = await faceServiceClient.IdentifyAsync(personGroupID, faceIds);
            //foreach (var identifyResult in results)
            //{
            //    Console.WriteLine("Result of face: {0}", identifyResult.FaceId);
            //    if (identifyResult.Candidates.Length == 0)
            //    {
            //        Console.WriteLine("No one identified");
            //    }
            //    else
            //    {
            //        // Get top 1 among all candidates returned
            //        var candidateId = identifyResult.Candidates[0].PersonId;
            //        var person = await faceServiceClient.GetPersonAsync(personGroupID, candidateId);
            //        Console.WriteLine("Identified as {0}", person.Name);
            //    }
            //}
            


        }

        private void LoginPage(object sender, RoutedEventArgs e)
        {
            this.Frame.Navigate(typeof(Login));
        }
    }
}
