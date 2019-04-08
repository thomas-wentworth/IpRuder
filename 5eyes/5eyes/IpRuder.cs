using DirectShowLib;
using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;

namespace _5Eyes
{
	public class IpRuder
	{
		private class SampleGrabberCallback : ISampleGrabberCB
		{
			private Bitmap lastImage { get; set; }

			public Int32 BufferCB ( Double sampleTime, IntPtr pBuffer, Int32 BufferLen )
			{
				return 0;
			}

			public Int32 SampleCB ( Double SampleTime, IMediaSample pSample )
			{
				if ( pSample == null )
				{
					return -1;
				}
				var len = pSample.GetActualDataLength ( );
				IntPtr pbuf;
				if ( pSample.GetPointer ( out pbuf ) == 0 && len > 0 )
				{
					var buf = new Byte [ len ];
					Marshal.Copy ( pbuf, buf, 0, len );
					var image = new Bitmap ( 640, 480 );
					var at = 0;
					if ( len % 3 != 0 )
					{
						// image is not bitmap 24bit, what now, burn it to the ground?
						return 0;
					}
					for ( var bOff = 0; bOff < len; bOff += 3 )
					{
						var color = Color.FromArgb ( buf [ bOff + 2 ], buf [ bOff + 1 ], buf [ bOff ] );
						var x = ( image.Width - 1 ) - ( at % image.Width );
						var y = ( image.Height - 1 ) - ( at / image.Width % image.Height );
						image.SetPixel ( x, y, color );
						at++;
					}
					lastImage?.Dispose ( );
					lastImage = image;
				}
				Marshal.ReleaseComObject ( pSample );
				return 0;
			}

			public Bitmap PickupImage ( )
			{
				var image = lastImage;
				lastImage = null;
				return image;
			}
		}

		public IpRuder ( String preferedCaptureDevice )
		{
			SampleGrabberCount = 0;
			PreferedCaptureDevice = preferedCaptureDevice;
			WakeProcessing = new ManualResetEvent ( false );
			ProccessingEvents = new Thread ( ( ) =>
			{
				try
				{
					EventCode ev;
					IntPtr p1, p2;
					while ( true )
					{
						WakeProcessing.WaitOne ( );
						while ( VideoEvent.GetEvent ( out ev, out p1, out p2, 0 ) == 0 )
						{
							try
							{
								if ( ev == EventCode.Complete || ev == EventCode.UserAbort )
								{
									StopVideo ( );
									break;
								}
								else if ( ev == EventCode.ErrorAbort )
								{
									Console.WriteLine ( "An error occured: HRESULT={0:X}", p1 );
									StopVideo ( );
									break;
								}
							}
							finally
							{
								VideoEvent.FreeEventParams ( ev, p1, p2 );
							}
						}
						while ( PhotoEvent.GetEvent ( out ev, out p1, out p2, 0 ) == 0 )
						{
							try
							{
								if ( ev == EventCode.Complete || ev == EventCode.UserAbort )
								{
									StopPhoto ( );
									break;
								}
								else if ( ev == EventCode.ErrorAbort )
								{
									Console.WriteLine ( "An error occured: HRESULT={0:X}", p1 );
									StopPhoto ( );
									break;
								}
								PhotoEvent.FreeEventParams ( ev, p1, p2 );
							}
							finally
							{
								PhotoEvent.FreeEventParams ( ev, p1, p2 );
							}
						}
						Thread.Sleep ( 500 );
					}
				}
				catch ( COMException ex )
				{
					Console.WriteLine ( "COM error: " + ex.ToString ( ) );
				}
				catch ( Exception ex )
				{
					Console.WriteLine ( "Error: " + ex.ToString ( ) );
				}
			} );
			ProccessingEvents.IsBackground = true;
			ProccessingEvents.Start ( );
			PhotoCallBack = new SampleGrabberCallback ( );
		}

		private Int32 SampleGrabberCount { get; set; }
		private String PreferedCaptureDevice { get; set; }
		private Int32 VideoCount { get; set; }
		private String VideoFolder { get; set; }
		private String PhotoFolder { get; set; }
		private String TempAvi { get; set; }
		private ManualResetEvent WakeProcessing { get; set; }
		private Thread ProccessingEvents { get; set; }
		private Boolean VideoRunning { get; set; }
		private Boolean PhotoRunning { get; set; }

		private IGraphBuilder VideoGraph { get; set; }
		private IGraphBuilder PhotoGraph { get; set; }
		private IMediaControl VideoControl { get; set; }
		private IMediaControl PhotoControl { get; set; }
		private IMediaEvent VideoEvent { get; set; }
		private IMediaEvent PhotoEvent { get; set; }
		private SampleGrabberCallback PhotoCallBack { get; set; }

		public void PrepareVideoCapture ( String videoFolder )
		{
			VideoFolder = videoFolder;
			if ( String.IsNullOrWhiteSpace ( VideoFolder ) )
			{
				throw new ArgumentException ( "Video folder must be specified." );
			}
			if ( !Directory.Exists ( VideoFolder ) )
			{
				Directory.CreateDirectory ( VideoFolder );
			}
			VideoGraph = ( IGraphBuilder ) new FilterGraph ( );
			VideoControl = ( IMediaControl ) VideoGraph;
			VideoEvent = ( IMediaEvent ) VideoGraph;
			TempAvi = Path.Combine ( Path.GetTempPath ( ), "tmp.avi" );
			BuildVideoGraph ( VideoGraph, TempAvi );
			VideoCount = 0;
		}

		public void PreparePhotoCapture ( String stillFolder )
		{
			PhotoFolder = stillFolder;
			if ( String.IsNullOrWhiteSpace ( PhotoFolder ) )
			{
				throw new ArgumentException ( "Still folder must be specified." );
			}
			if ( !Directory.Exists ( PhotoFolder ) )
			{
				Directory.CreateDirectory ( PhotoFolder );
			}
			PhotoGraph = ( IGraphBuilder ) new FilterGraph ( );
			PhotoControl = ( IMediaControl ) PhotoGraph;
			PhotoEvent = ( IMediaEvent ) PhotoGraph;
			BuildStillGraph ( PhotoGraph );
		}

		public void StartVideo ( )
		{
			if ( VideoRunning )
			{
				return;
			}
			VideoRunning = true;
			if ( PhotoRunning )
			{
				StopPhoto ( );
			}
			var hr = VideoControl.Run ( );
			checkHR ( hr, "Failed running graph" );
			WakeProcessing.Set ( );
		}

		public void StopVideo ( )
		{
			var hr = VideoControl.Stop ( );
			checkHR ( hr, "Failed stopping graph" );
			File.Move ( TempAvi, Path.Combine ( VideoFolder, VideoCount++ + ".avi" ) );
			VideoRunning = false;
			CleanupStop ( );
		}

		public void StartPhoto ( )
		{
			if ( PhotoRunning )
			{
				return;
			}
			PhotoRunning = true;
			if ( VideoRunning )
			{
				StopVideo ( );
			}
			var hr = PhotoControl.Run ( );
			checkHR ( hr, "Failed running graph" );
			WakeProcessing.Set ( );
		}

		public void StopPhoto ( )
		{
			var hr = PhotoControl.Stop ( );
			checkHR ( hr, "Failed stopping graph" );
			PhotoRunning = false;
			CleanupStop ( );
		}

		public Bitmap RetrievePhoto ( )
		{
			return PhotoCallBack.PickupImage ( );
		}

		public Boolean VideoIsRunning ( )
		{
			return VideoRunning;
		}

		public Boolean PhotoIsRunning ( )
		{
			return PhotoRunning;
		}

		private void CleanupStop ( )
		{
			if ( !VideoRunning && !PhotoRunning )
			{
				WakeProcessing.Reset ( );
			}
		}

		private void checkHR ( Int32 hr, String msg )
		{
			if ( hr != 0 )
			{
				DsError.ThrowExceptionForHR ( hr );
			}
		}

		private IBaseFilter BuildSampleGrabber ( IGraphBuilder pGraph )
		{
			var CLSID_SampleGrabber = new Guid ( "{C1F400A0-3F08-11D3-9F0B-006008039E37}" ); //qedit.dll
			var hr = 0;
			//add SampleGrabber
			var pSampleGrabber = ( IBaseFilter ) Activator.CreateInstance ( Type.GetTypeFromCLSID ( CLSID_SampleGrabber ) );
			hr = pGraph.AddFilter ( pSampleGrabber, "SampleGrabber" + SampleGrabberCount++ );
			checkHR ( hr, String.Format ( "Failed adding SampleGrabber {0} to graph", SampleGrabberCount ) );
			var pSampleGrabber_pmt = new AMMediaType ( );
			pSampleGrabber_pmt.majorType = MediaType.Video;
			pSampleGrabber_pmt.subType = MediaSubType.RGB24;
			pSampleGrabber_pmt.formatType = FormatType.VideoInfo;
			pSampleGrabber_pmt.fixedSizeSamples = true;
			pSampleGrabber_pmt.formatSize = 88;
			pSampleGrabber_pmt.sampleSize = 460800;
			pSampleGrabber_pmt.temporalCompression = false;
			var pSampleGrabber_format = new VideoInfoHeader ( );
			pSampleGrabber_format.SrcRect = new DsRect ( );
			pSampleGrabber_format.TargetRect = new DsRect ( );
			pSampleGrabber_format.BitRate = 110592000;
			pSampleGrabber_format.AvgTimePerFrame = 333333;
			pSampleGrabber_format.BmiHeader = new BitmapInfoHeader ( );
			pSampleGrabber_format.BmiHeader.Size = 40;
			pSampleGrabber_format.BmiHeader.Width = 640;
			pSampleGrabber_format.BmiHeader.Height = 480;
			pSampleGrabber_format.BmiHeader.Planes = 1;
			pSampleGrabber_format.BmiHeader.BitCount = 12;
			pSampleGrabber_format.BmiHeader.Compression = 808596553;
			pSampleGrabber_format.BmiHeader.ImageSize = 460800;
			pSampleGrabber_pmt.formatPtr = Marshal.AllocCoTaskMem ( Marshal.SizeOf ( pSampleGrabber_format ) );
			Marshal.StructureToPtr ( pSampleGrabber_format, pSampleGrabber_pmt.formatPtr, false );
			hr = ( ( ISampleGrabber ) pSampleGrabber ).SetMediaType ( pSampleGrabber_pmt );
			DsUtils.FreeAMMediaType ( pSampleGrabber_pmt );
			checkHR ( hr, "Failed setting media type to sample grabber" );
			return pSampleGrabber;
		}

		private void BuildVideoGraph ( IGraphBuilder pGraph, String dstFile )
		{
			var hr = 0;

			//graph builder
			var pBuilder = ( ICaptureGraphBuilder2 ) new CaptureGraphBuilder2 ( );
			hr = pBuilder.SetFiltergraph ( pGraph );
			checkHR ( hr, "Failed SetFilterGraph" );

			//add LogitechHD Webcam C270
			var pLogitechHDWebcamC270 = CreateFilterByName ( PreferedCaptureDevice, FilterCategory.VideoInputDevice );
			hr = pGraph.AddFilter ( pLogitechHDWebcamC270, PreferedCaptureDevice ?? "Cappy" );
			checkHR ( hr, String.Format ( "Failed adding {0} to graph", PreferedCaptureDevice ?? "Cappy" ) );
			//add SampleGrabber
			var pSampleGrabberWithCallback = BuildSampleGrabber ( pGraph );

			//connect Logitech HD Webcam C270 and SampleGrabber
			hr = ( ( ISampleGrabber ) pSampleGrabberWithCallback ).SetCallback ( PhotoCallBack, 0 );
			checkHR ( hr, "Failed setting SampleGrabber callback" );
			hr = pGraph.ConnectDirect ( GetPin ( pLogitechHDWebcamC270, "Capture" ), GetPin ( pSampleGrabberWithCallback, "Input" ), null );
			checkHR ( hr, String.Format ( "Failed connecting {0} and SampleGrabber", PreferedCaptureDevice ?? "Cappy" ) );

			//add File writer
			var pFilewriter = ( IBaseFilter ) new FileWriter ( );
			hr = pGraph.AddFilter ( pFilewriter, "File writer" );
			checkHR ( hr, "Failed adding File writer to graph" );
			//set destination filename
			var pFilewriter_sink = ( IFileSinkFilter ) pFilewriter;
			hr = pFilewriter_sink.SetFileName ( dstFile, null );
			checkHR ( hr, "Failed setting filename" );

			//add AVI Mux
			var pAVIMux = ( IBaseFilter ) new AviDest ( );
			hr = pGraph.AddFilter ( pAVIMux, "AVI Mux" );
			checkHR ( hr, "Failed adding AVI Mux to graph" );

			//connect SampleGrabber and AVI Mux
			hr = pGraph.ConnectDirect ( GetPin ( pSampleGrabberWithCallback, "Output" ), GetPin ( pAVIMux, "Input 01" ), null );
			checkHR ( hr, "Failed connecting SampleGrabber and AVI Mux" );

			//connect AVI Mux and File writer
			hr = pGraph.ConnectDirect ( GetPin ( pAVIMux, "AVI Out" ), GetPin ( pFilewriter, "in" ), null );
			checkHR ( hr, "Failed connecting AVI Mux and File writer" );
		}

		private void BuildStillGraph ( IGraphBuilder pGraph )
		{
			var hr = 0;

			//graph builder
			var pBuilder = ( ICaptureGraphBuilder2 ) new CaptureGraphBuilder2 ( );
			hr = pBuilder.SetFiltergraph ( pGraph );
			checkHR ( hr, "Failed SetFilterGraph" );

			//add LogitechHD Webcam C270
			var pLogitechHDWebcamC270 = CreateFilterByName ( PreferedCaptureDevice, FilterCategory.VideoInputDevice );
			hr = pGraph.AddFilter ( pLogitechHDWebcamC270, PreferedCaptureDevice ?? "Cappy" );
			checkHR ( hr, String.Format ( "Failed adding {0} to graph", PreferedCaptureDevice ?? "Cappy" ) );
			//add SampleGrabber
			var pSampleGrabberWithCallback = BuildSampleGrabber ( pGraph );

			//connect Logitech HD Webcam C270 and SampleGrabber
			hr = ( ( ISampleGrabber ) pSampleGrabberWithCallback ).SetCallback ( PhotoCallBack, 0 );
			checkHR ( hr, "Failed setting SampleGrabber callback" );
			/*var stillPin = DsFindPin.ByName ( pLogitechHDWebcamC270, "Still" ) ??
				DsFindPin.ByName ( pLogitechHDWebcamC270, "Capture" );
			if ( stillPin == null )
			{
				checkHR ( -1, "Failed finding a capture pin" );
			}
			hr = pGraph.ConnectDirect ( stillPin, GetPin ( pSampleGrabberWithCallback, "Input" ), null );*/
			hr = pGraph.ConnectDirect ( GetPin ( pLogitechHDWebcamC270, "Capture" ), GetPin ( pSampleGrabberWithCallback, "Input" ), null );
			checkHR ( hr, String.Format ( "Failed connecting {0} and SampleGrabber", PreferedCaptureDevice ?? "Cappy" ) );
		}

		private IPin GetPin ( IBaseFilter filter, String pinName )
		{
			var pin = DsFindPin.ByName ( filter, pinName );
			if ( pin == null )
			{
				checkHR ( -1, String.Format ( "Pin: {0} not found", pinName ) );
				return null;
			}
			return pin;
		}

		private IBaseFilter CreateFilterByName ( String filterName, Guid category )
		{
			var hr = 0;
			var devices = DsDevice.GetDevicesOfCat ( category );
			IBaseFilter lastFilter = null;
			foreach ( var dev in devices )
			{
				if ( dev.Name == filterName || lastFilter == null )
				{
					IBaseFilter filter = null;
					IBindCtx bindCtx = null;
					try
					{
						hr = CreateBindCtx ( 0, out bindCtx );
						checkHR ( hr, "Failed binding ctx" );
						var guid = typeof ( IBaseFilter ).GUID;
						object obj;
						dev.Mon.BindToObject ( bindCtx, null, ref guid, out obj );
						filter = ( IBaseFilter ) obj;
					}
					finally
					{
						if ( bindCtx != null )
						{
							Marshal.ReleaseComObject ( bindCtx );
						}
					}
					lastFilter = filter;

					if ( String.IsNullOrWhiteSpace ( filterName ) || dev.Name == filterName )
					{
						return filter;
					}
				}
			}
			return lastFilter;
		}

		[DllImport ( "ole32.dll" )]
		public static extern Int32 CreateBindCtx ( Int32 reserved, out IBindCtx ppbc );
	}
}