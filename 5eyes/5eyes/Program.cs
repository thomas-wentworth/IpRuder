using System;

namespace _5Eyes
{
	class Program
	{
		static void Main ( String [ ] args )
		{
			// never do this never, but...
			var cameraLogic = new CameraLogic ( args [ 0 ], args [ 1 ], args [ 2 ], Boolean.Parse( args [ 3 ] ), Boolean.Parse( args [ 4 ] ) );
			cameraLogic.StartCapturing ( );
			Console.WriteLine ( "Press q to stop" );
			while ( Console.ReadKey ( ).KeyChar != 'q' ) ;
			cameraLogic.StopCapturing ( );
		}
	}
}