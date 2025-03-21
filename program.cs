using System;

class Program
{
    static void Main(string[] args)
    {
        string firebaseUrl = "https://databasec-b35a7-default-rtdb.firebaseio.com/"; // Replace with your actual Firebase URL
        string patientId = "temp_patient";  // This will be updated dynamically

        KinectHelper? kinect = null;

        try
        {
            kinect = new KinectHelper(firebaseUrl, patientId);
            if (kinect != null)
            {
                kinect.StartTrackingHeight();
                Console.WriteLine("📏 Height measurement started. Press Enter to stop...");
                Console.ReadLine();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error initializing Kinect: {ex.Message}");
        }
        finally
        {
            kinect?.Stop();
            Console.WriteLine("🛑 Kinect stopped.");
        }
    }
}
