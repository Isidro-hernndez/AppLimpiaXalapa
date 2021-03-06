using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Provider;

using AppLimpia.Media;

using Path = System.IO.Path;

namespace AppLimpia.Droid
{
    /// <summary>
    /// Android <see cref="MediaPicker"/> implementation.
    /// </summary>
    public sealed class MediaPickerDroid : MediaPicker
    {
        /// <summary>
        /// The request identifier.
        /// </summary>
        private int requestId;

        /// <summary>
        /// The task completion source.
        /// </summary>
        private TaskCompletionSource<MediaFile> completionSource;

        /// <summary>
        /// Initializes a new instance of the <see cref="MediaPickerDroid"/> class.
        /// </summary>
        public MediaPickerDroid()
        {
            // Detect if camera present
            var isCameraAvailable =
                MediaPickerDroid.Context.PackageManager.HasSystemFeature(PackageManager.FeatureCamera);
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Gingerbread)
            {
                isCameraAvailable |=
                    MediaPickerDroid.Context.PackageManager.HasSystemFeature(PackageManager.FeatureCameraFront);
            }

            this.IsCameraAvailable = isCameraAvailable;
            this.IsPhotosSupported = true;
        }

        /// <summary> 
        /// Gets a value indicating whether this instance is camera available. 
        /// </summary> 
        public override bool IsCameraAvailable { get; }

        /// <summary> 
        /// Gets a value indicating whether this instance is photos supported. 
        /// </summary> 
        public override bool IsPhotosSupported { get; }

        /// <summary>
        /// Gets the current execution context.
        /// </summary>
        private static Context Context => Application.Context;

        /// <summary> 
        /// Takes the picture. 
        /// </summary> 
        /// <param name="options">The storage options.</param> 
        /// <returns>Task representing the asynchronous operation.</returns>
        /// <exception cref="NotSupportedException">No camera is present on the current device.</exception>
        public override Task<MediaFile> TakePhotoAsync(CameraMediaStorageOptions options)
        {
            // If no camera available
            if (!this.IsCameraAvailable)
            {
                throw new NotSupportedException();
            }

            // Validate options
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            // If path is rooted
            if (Path.IsPathRooted(options.Directory))
            {
                throw new ArgumentException("options.Directory must be a relative folder");
            }

            // Take the photo
            return this.TakeMediaAsync("image/*", MediaStore.ActionImageCapture, options);
        }

        /// <summary>
        /// Resizes the image to have the specified maximum sizes.
        /// </summary>
        /// <param name="source">The source image to resize.</param>
        /// <param name="maxWidth">The max width of the image.</param>
        /// <param name="maxHeight">The max height of the image.</param>
        /// <returns>The resized image data.</returns>
        public override Stream ResizeImage(Stream source, int maxWidth, int maxHeight)
        {
            // Load the bitmap
            using (var originalBitmap = BitmapFactory.DecodeStream(source))
            {
                // If the image does not need to be resized
                var originalWidth = originalBitmap.Width;
                var originalHeight = originalBitmap.Height;
                if ((originalWidth <= maxWidth) && (originalHeight <= maxHeight))
                {
                    System.Diagnostics.Debug.WriteLine("O: Width = {0}; Height = {1}", originalWidth, originalHeight);
                    System.Diagnostics.Debug.WriteLine("No resize");
                    return source;
                }

                // Calculate the resized image size
                float resizedHeight = originalHeight;
                float resizedWidth = originalWidth;

                // If height is greater than the maximum height
                if (resizedHeight > maxHeight)
                {
                    resizedHeight = maxHeight;
                    var factor = (float)originalHeight / maxHeight;
                    resizedWidth = originalWidth / factor;
                }

                // If the width is greater than the maximum width
                if (resizedWidth > maxWidth)
                {
                    resizedWidth = maxWidth;
                    var factor = (float)originalWidth / maxWidth;
                    resizedHeight = originalHeight / factor;
                }

                System.Diagnostics.Debug.WriteLine("O: Width = {0}; Height = {1}", originalWidth, originalHeight);
                System.Diagnostics.Debug.WriteLine(
                    "R: Width = {0}; Height = {1}",
                    (int)resizedWidth,
                    (int)resizedHeight);

                // Resize the image
                var resizedBitmap = Bitmap.CreateScaledBitmap(
                    originalBitmap,
                    (int)resizedWidth,
                    (int)resizedHeight,
                    false);

                // Save the rescaled image
                using (resizedBitmap)
                {
                    var imageData = new MemoryStream();
                    resizedBitmap.Compress(Bitmap.CompressFormat.Jpeg, 95, imageData);
                    return imageData;
                }
            }
        }

        /// <summary>
        /// Takes the media asynchronous.
        /// </summary>
        /// <param name="type">The type of intent.</param>
        /// <param name="action">The action.</param>
        /// <param name="options">The options.</param>
        /// <returns>Task with a return type of MediaFile.</returns>
        /// <exception cref="System.InvalidOperationException">Only one operation can be active at a time.</exception>
        private Task<MediaFile> TakeMediaAsync(string type, string action, CameraMediaStorageOptions options)
        {
            // Create the completion source
            var id = this.GetRequestId();
            var source = new TaskCompletionSource<MediaFile>(id);
            if (Interlocked.CompareExchange(ref this.completionSource, source, null) != null)
            {
                throw new InvalidOperationException("Only one operation can be active at a time");
            }

            // Start the media activity
            MediaPickerDroid.Context.StartActivity(this.CreateMediaIntent(id, type, action, options));

            // Set the media picked event handler
            EventHandler<MediaPickerActivity.MediaPickedEventArgs> handler = null;
            handler = (s, e) =>
            {
                // Remove the handler
                var taskCompletion = Interlocked.Exchange(ref this.completionSource, null);
                MediaPickerActivity.MediaPicked -= handler;

                // Validate the request identifier
                if (e.RequestId != id)
                {
                    return;
                }

                // Set the task result
                if (e.Error != null)
                {
                    taskCompletion.SetException(e.Error);
                }
                else if (e.IsCanceled)
                {
                    taskCompletion.SetCanceled();
                }
                else
                {
                    taskCompletion.SetResult(e.Media);
                }
            };

            // Register the handler
            MediaPickerActivity.MediaPicked += handler;
            return source.Task;
        }

        /// <summary>
        /// Gets the request identifier.
        /// </summary>
        /// <returns>Request id as integer.</returns>
        private int GetRequestId()
        {
            // Get the next request identifier
            var id = this.requestId;
            if (this.requestId == int.MaxValue)
            {
                this.requestId = 0;
            }
            else
            {
                this.requestId++;
            }

            return id;
        }

        /// <summary>
        /// Creates the media intent.
        /// </summary>
        /// <param name="id">The identifier.</param>
        /// <param name="type">The type of intent.</param>
        /// <param name="action">The action.</param>
        /// <param name="options">The options.</param>
        /// <param name="tasked">if set to <c>true</c> [tasked].</param>
        /// <returns>Intent to create media.</returns>
        private Intent CreateMediaIntent(
            int id,
            string type,
            string action,
            CameraMediaStorageOptions options,
            bool tasked = true)
        {
            // Create the intent
            var pickerIntent = new Intent(MediaPickerDroid.Context, typeof(MediaPickerActivity));
            pickerIntent.SetFlags(ActivityFlags.NewTask);
            pickerIntent.PutExtra(MediaPickerActivity.ExtraId, id);
            pickerIntent.PutExtra(MediaPickerActivity.ExtraType, type);
            pickerIntent.PutExtra(MediaPickerActivity.ExtraAction, action);
            pickerIntent.PutExtra(MediaPickerActivity.ExtraTasked, tasked);

            // Set the additional options
            if (options != null)
            {
                pickerIntent.PutExtra(MediaPickerActivity.ExtraPath, options.Directory);
                pickerIntent.PutExtra(MediaStore.Images.ImageColumns.Title, options.Name);
            }

            // Remove the created intent
            return pickerIntent;
        }
    }
}