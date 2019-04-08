using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Threading;

namespace _5Eyes
{
	public class CameraLogic
	{
		private IpRuder ip;
		private Thread cameraThread;
		private Thread videoTread;
		private Thread fileSaveThread;
		private ConcurrentQueue<BitmapOutput> photos;
		private ManualResetEvent wakeCamera;
		private AutoResetEvent wakeVideo;
		private AutoResetEvent wakeFile;
		private Int32 varience;
		private Int32 threshold;
		private Int64 milisecondRecordTime;
		private Boolean saveVideo;
		private Boolean savePhoto;
		private Boolean runCamera;
		private Boolean runVideo;
		private Boolean runFile;
		private String photoFolder;
		private String videoFolder;

		public CameraLogic ( String prefreredDeviceName, String photoFolder = null, String videoFolder = null, Boolean savePhoto = true,
			Boolean saveVideo = false, Int32 varience = 25, Int32 threshold = 100, Int64 recordOffsetMS = 180000 )
		{
			ip = new IpRuder ( prefreredDeviceName );
			cameraThread = new Thread ( CameraCallback );
			videoTread = new Thread ( VideoCallback );
			fileSaveThread = new Thread ( FileCallback );
			photos = new ConcurrentQueue<BitmapOutput> ( );
			wakeCamera = new ManualResetEvent ( false );
			wakeVideo = new AutoResetEvent ( false );
			wakeFile = new AutoResetEvent ( false );
			this.varience = varience;
			this.threshold = threshold;
			milisecondRecordTime = recordOffsetMS;
			this.saveVideo = saveVideo;
			this.savePhoto = savePhoto;
			this.photoFolder = photoFolder;
			this.videoFolder = videoFolder;

			if ( ( savePhoto && String.IsNullOrWhiteSpace ( photoFolder ) ) || ( saveVideo && String.IsNullOrWhiteSpace ( videoFolder ) )
				|| varience < 1 || threshold < 1 || recordOffsetMS < 15000 )
			{
				throw new ArgumentException ( "Invaild options" );
			}

			prevBm = new Bitmap ( 1, 1 );
			nextBm = new Bitmap ( 1, 1 );
			curBm = new Bitmap ( 1, 1 );
			lastDiff = null;
		}

		public void StartCapturing ( )
		{
			if ( savePhoto )
			{
				fileSaveThread.Start ( );
			}
			if ( saveVideo )
			{
				videoTread.Start ( );
			}
			wakeCamera.Set ( );
			cameraThread.Start ( );
		}

		public void StopCapturing ( )
		{
			runCamera = false;
			runVideo = false;
			runFile = false;
			wakeCamera.Set ( ); ;
			if ( saveVideo )
			{
				wakeVideo.Set ( );
				videoTread.Join ( 500 );
			}
			if ( savePhoto )
			{
				wakeFile.Set ( );
				fileSaveThread.Join ( 500 );
			}
			cameraThread.Join ( 500 );
		}

		private void CameraCallback ( )
		{
			runCamera = true;
			try
			{
				while ( runCamera )
				{
					try
					{
						ip.PreparePhotoCapture ( photoFolder );
						while ( runCamera )
						{
							wakeCamera.WaitOne ( );
							ip.StartPhoto ( );
							if ( Tick ( ) && saveVideo )
							{
								ip.StopPhoto ( );
								wakeCamera.Reset ( );
								wakeVideo.Set ( );
								continue;
							}
						}
					}
					catch ( Exception ex )
					{
						Console.WriteLine ( ex.ToString ( ) );
					}
				}
			}
			catch ( Exception )
			{ }
		}

		private void VideoCallback ( )
		{
			runVideo = true;
			try
			{
				while ( runVideo )
				{
					try
					{
						ip.PrepareVideoCapture ( videoFolder );
						while ( runVideo )
						{
							wakeVideo.WaitOne ( );
							var start = DateTime.Now;
							ip.StartVideo ( );
							while ( runVideo && ( ( DateTime.Now - start ).TotalMilliseconds < milisecondRecordTime ) )
							{
								if ( Tick ( ) )
								{
									start = DateTime.Now;
								}
							}
							ip.StopVideo ( );
							wakeCamera.Set ( );
						}
					}
					catch ( Exception ex )
					{
						Console.WriteLine ( ex.ToString ( ) );
					}
					finally
					{
						ip.StopVideo ( );
					}
				}
			}
			catch ( Exception )
			{ }
		}

		private void FileCallback ( )
		{
			runFile = true;
			BitmapOutput outBitmap;
			try
			{
				while ( runFile )
				{
					try
					{
						wakeFile.WaitOne ( );
						while ( runFile && photos.TryDequeue ( out outBitmap ) )
						{
							var dir = Path.Combine ( photoFolder, outBitmap.DateTime.ToString ( "yyyyMMdd" ) );
							if ( !Directory.Exists ( dir ) )
							{
								Directory.CreateDirectory ( dir );
							}
							outBitmap.Bitmap.Save ( Path.Combine ( dir, String.Format ( outBitmap.FileName, outBitmap.DateTime.ToString ( "HHmmssf" ) ) ) );
						}
					}
					catch ( Exception )
					{ }
				}
			}
			catch ( Exception )
			{ }
		}

		private Boolean CheckPixels ( Bitmap src, Int32 threshold, Int32 variance )
		{
			try
			{
				var differentPoints = 0;
				var varPix = Color.FromArgb ( variance, variance, variance );
				for ( var w = 0; w < src.Width; w++ )
				{
					for ( var h = 0; h < src.Height; h++ )
					{
						var srcPix = src.GetPixel ( w, h );
						if ( !( srcPix.R <= varPix.R && srcPix.G <= varPix.G && srcPix.B <= varPix.B ) )
						{
							if ( ++differentPoints >= threshold )
							{
								return true;
							}
						}
					}
				}
			}
			catch ( Exception ) { }
			return false;
		}

		private Bitmap ConvertToGreyScale ( Bitmap src )
		{
			try
			{
				var greyBitmap = new Bitmap ( src.Width, src.Height );
				for ( var w = 0; w < src.Width; w++ )
				{
					for ( var h = 0; h < src.Height; h++ )
					{
						var pix = src.GetPixel ( w, h );
						var avg = ( pix.R + pix.G + pix.B ) / 3;
						var outPix = Color.FromArgb ( avg, avg, avg );
						greyBitmap.SetPixel ( w, h, outPix );
					}
				}
				return greyBitmap;
			}
			catch ( Exception ) { }
			return null;
		}

		private Bitmap Difference ( Bitmap _0, Bitmap _1 )
		{
			try
			{
				var width = _0.Width < _1.Width ? _0.Width : _1.Width;
				var height = _0.Height < _1.Height ? _0.Height : _1.Height;
				Bitmap diff = new Bitmap ( width, height );
				for ( var w = 0; w < width; w++ )
				{
					for ( var h = 0; h < height; h++ )
					{
						var pix0 = _0.GetPixel ( w, h );
						var pix1 = _1.GetPixel ( w, h );
						var pixDiff = Math.Abs ( pix0.R - pix1.R );
						var outPix = Color.FromArgb ( pixDiff, pixDiff, pixDiff );
						diff.SetPixel ( w, h, outPix );
					}
				}
				return diff;
			}
			catch ( Exception ) { }
			return null;
		}

		private Bitmap BitwiseAnd ( Bitmap _0, Bitmap _1 )
		{
			try
			{
				var width = _0.Width < _1.Width ? _0.Width : _1.Width;
				var height = _0.Height < _1.Height ? _0.Height : _1.Height;
				Bitmap and = new Bitmap ( width, height );
				for ( var w = 0; w < width; w++ )
				{
					for ( var h = 0; h < height; h++ )
					{
						var pix0 = _0.GetPixel ( w, h );
						var pix1 = _1.GetPixel ( w, h );
						var pixAnd = pix0.R & pix1.R;
						var outPix = Color.FromArgb ( pixAnd, pixAnd, pixAnd );
						and.SetPixel ( w, h, outPix );
					}
				}
				return and;
			}
			catch ( Exception ) { }
			return null;
		}

		private Bitmap prevBm;
		private Bitmap nextBm;
		private Bitmap curBm;
		private Bitmap lastDiff;

		private Boolean Tick ( )
		{
			var next = ip.RetrievePhoto ( );
			if ( next == null )
			{
				return false;
			}
			prevBm.Dispose ( );
			prevBm = curBm;
			curBm = nextBm;
			nextBm = ConvertToGreyScale ( next );
			var diff0 = lastDiff ?? Difference ( prevBm, curBm );
			var diff1 = Difference ( curBm, nextBm );
			diff0 = BitwiseAnd ( diff0, diff1 );
			lastDiff = diff1;
			if ( CheckPixels ( diff0, threshold, varience ) )
			{
				Console.WriteLine ( "Detected." );

				if ( savePhoto )
				{
					var outputBitmap = new BitmapOutput ( next, "Int.{0}.bmp", DateTime.Now );
					photos.Enqueue ( outputBitmap );
					wakeFile.Set ( );
				}

				return true;
			}
			return false;
		}

		private class BitmapOutput
		{
			public BitmapOutput ( Bitmap bitmap, String fileName, DateTime dateTime )
			{
				Bitmap = bitmap;
				FileName = fileName;
				DateTime = dateTime;
			}

			public Bitmap Bitmap { get; set; }
			public String FileName { get; set; }
			public DateTime DateTime { get; set; }
		}
	}
}