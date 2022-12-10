/*
My camera is scanning a long tool, up to 4 ft long.
It gets its line triggers from the Shaft Encoder Software Module which gets input from a quadrature encoder coming in on input lines 1 and 2.
A sensor on input line 3, that detects when the camera arm lowers into position and when it raises again, is used for the AcquisitionStart and AcquisitionStop trigger.
I have confirmed that all these inputs are working correctly by setting up a timer that polls the value of the LineStatusAll parameter every 500 ms.
That works just fine.
The entire scanning time for the image can take anywhere from 30 to 60 seconds, depending on the speed the operator has set.
When the camera arm goes down and Line 3 goes Hi, I want to issue the AcquisitionStart command and immediately start acquiring the image (FrameStart TriggerMode is Off).
When the camera arm goes back up and Line 3 turns off, that’s when I want to stop the acquisition and immediately grab the image from the camera.
What is the best way to do that? 
*/

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Numerics;

using System.Collections;
using System.IO;

//Added references
using System.Xml;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Runtime.InteropServices;

using System.Threading;

using Basler.Pylon;


namespace BaslerRacerTest_pylon
{
    public partial class frmMain : Form
    {
        public Camera cam;

        IGrabResult grabResult;

        string strCamVendor, strCamModel, strCamFirmware;

        long intAOIOffsetX, intAOIOffsetY, intAOIWidth, intAOIHeight, lngCamLineStatusAll, lngEncoderPosition;

        double exposureTime = 0;

        int width = 640;
        int height = 240;

        bool isLowered = false;

        bool isInitialized = false;

        public static int intGoodGrabCount, intBadGrabCount;

        List<Bitmap> images = new List<Bitmap>();

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                // If called from a different thread, we must use the Invoke method to marshal the call to the proper thread.
                BeginInvoke(new EventHandler<EventArgs>(checkBox1_CheckedChanged), sender, e);
                return;
            }

            try
            {
                if (checkBox1.Checked == true)
                {
                    // A lowered arm sends a hardware trigger to the camera. Simulate that by toggling the inverter
                    cam.Parameters[PLCamera.LineSelector].SetValue(PLCamera.LineSelector.Line3);
                    cam.Parameters[PLCamera.LineInverter].SetValue(!cam.Parameters[PLCamera.LineInverter].GetValue()); // low -> high
                    isLowered = true;
                }
                else
                {
                    // A raised arm stops the hardware trigger to the camera. Simulate that by toggling the inverter
                    cam.Parameters[PLCamera.LineSelector].SetValue(PLCamera.LineSelector.Line3);
                    cam.Parameters[PLCamera.LineInverter].SetValue(!cam.Parameters[PLCamera.LineInverter].GetValue()); // low -> high
                    isLowered = false;
                }
            }
            catch (Exception ex)
            {
                tbError.AppendText("\r\n");
                tbError.AppendText(ex.Message.ToString());
            }

        }

        private void checkBox2_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                if (checkBox2.Checked == true)
                {
                    cam.Parameters[PLCamera.TriggerSelector].SetValue(PLCamera.TriggerSelector.AcquisitionStart);
                    cam.Parameters[PLCamera.TriggerMode].SetValue(PLCamera.TriggerMode.Off);
                }
                else
                {
                    cam.Parameters[PLCamera.TriggerSelector].SetValue(PLCamera.TriggerSelector.AcquisitionStart);
                    cam.Parameters[PLCamera.TriggerMode].SetValue(PLCamera.TriggerMode.On);
                }
            }
            catch (Exception ex)
            {
                tbError.AppendText("\r\n");
                tbError.AppendText(ex.Message.ToString());
            }
        }

        private void checkBox3_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                if (checkBox3.Checked == true)
                    cam.Parameters[PLCamera.TestImageSelector].SetValue(PLCamera.TestImageSelector.Testimage2);
                else
                    cam.Parameters[PLCamera.TestImageSelector].SetValue(PLCamera.TestImageSelector.Off);
            }
            catch (Exception ex)
            {
                tbError.AppendText("\r\n");
                tbError.AppendText(ex.Message.ToString());
            }
        }

        private void frmMain_Load(object sender, EventArgs e)
        {

            intGoodGrabCount = 0;
            intBadGrabCount = 0;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            intGoodGrabCount = 0;
            intBadGrabCount = 0;
            tbxGoodGrabCount.Text = intGoodGrabCount.ToString();
            tbxBadGrabCount.Text = intBadGrabCount.ToString();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            InitializeCam();
        }

        private void frmMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (cam != null)
            {
                cam.Close();
            }
        }

        private Bitmap Combine(List<Bitmap> images)
        {
            System.Drawing.Bitmap finalImage = null;

            try
            {
                int width = 0;
                int height = 0;

                //create a bitmap to hold the combined image
                finalImage = new Bitmap(images[0].Width, images[0].Height * 2, PixelFormat.Format32bppRgb);

                //get a graphics object from the image so we can draw on it
                using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(finalImage))
                {
                    //set background color
                    g.Clear(System.Drawing.Color.Black);

                    // stitch the two images top to bottom
                    int offset = 0;
                    foreach (System.Drawing.Bitmap image in images)
                    {
                        g.DrawImage(image,new System.Drawing.Rectangle(0, offset, image.Width, image.Height));
                        offset += image.Height;
                    }
                }

                return finalImage;
            }
            catch (Exception ex)
            {
                if (finalImage != null)
                    finalImage.Dispose();

                tbError.AppendText("\r\n");
                tbError.AppendText(ex.Message.ToString());

                return finalImage;
            }
        }

        private void ShowStitched()
        {
            try
            {
                Bitmap stitched = Combine(images);

                Bitmap bitmapOlde = pictureBox3.Image as Bitmap;
                pictureBox3.Image = stitched;

                if (bitmapOlde != null)
                {
                    // Dispose the bitmap.
                    bitmapOlde.Dispose();
                }
            }
            catch (Exception ex)
            {
                tbError.AppendText("\r\n");
                tbError.AppendText(ex.Message.ToString());
            }
        }

        private void OnImageGrabbed(Object sender, ImageGrabbedEventArgs e)
        {
            if (InvokeRequired)
            {
                // If called from a different thread, we must use the Invoke method to marshal the call to the proper GUI thread.
                // The grab result will be disposed after the event call. Clone the event arguments for marshaling to the GUI thread.
                BeginInvoke(new EventHandler<ImageGrabbedEventArgs>(OnImageGrabbed), sender, e.Clone());
                return;
            }

            try
            {
                // Get the grab result.
                IGrabResult grabResult = e.GrabResult;

                // Process the result
                if (grabResult.GrabSucceeded)
                {
                    Basler.Pylon.PixelDataConverter converter = new PixelDataConverter();
                    Bitmap bitmap = new Bitmap(grabResult.Width, grabResult.Height, PixelFormat.Format32bppRgb);
                    // Lock the bits of the bitmap.
                    BitmapData bmpData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, bitmap.PixelFormat);
                    // Place the pointer to the buffer of the bitmap.
                    converter.OutputPixelFormat = PixelType.BGRA8packed;
                    IntPtr ptrBmp = bmpData.Scan0;
                    converter.Convert(ptrBmp, bmpData.Stride * bitmap.Height, grabResult);
                    bitmap.UnlockBits(bmpData);

                    images.Add(bitmap);

                    if (grabResult.ImageNumber % 2 != 0)
                    {
                        // Assign a temporary variable to dispose the bitmap after assigning the new bitmap to the display control.
                        Bitmap bitmapOld = pictureBox1.Image as Bitmap;
                        // Provide the display control with the new bitmap. This action automatically updates the display.
                        pictureBox1.Image = bitmap;
                        if (bitmapOld != null)
                        {
                            // Dispose the bitmap.
                            bitmapOld.Dispose();
                        }
                    }
                    else
                    {
                        // Assign a temporary variable to dispose the bitmap after assigning the new bitmap to the display control.
                        Bitmap bitmapOld = pictureBox2.Image as Bitmap;
                        // Provide the display control with the new bitmap. This action automatically updates the display.
                        pictureBox2.Image = bitmap;
                        if (bitmapOld != null)
                        {
                            // Dispose the bitmap.
                            bitmapOld.Dispose();
                        }
                        ShowStitched();
                        images.Clear();
                    }

                    grabResult.Dispose();
                    // IImage img = grabResult.GetImage;    THE GETIMAGE FUNCTION DESCRIBED IN THE DOCUMENTATION IS NOT AVAILABLE!!
                    // So how do we go about saving the image once we're able to Grab the image successfully?? (which we haven't been able to do yet!)
                    intGoodGrabCount += 1;
                    tbxGoodGrabCount.Text = intGoodGrabCount.ToString();
                }
                else
                {
                    intBadGrabCount += 1;
                    tbxBadGrabCount.Text = intBadGrabCount.ToString();

                    tbError.AppendText("\r\n" + "Error: " + grabResult.ErrorCode.ToString() + "   " + grabResult.ErrorDescription.ToString());
                    //MessageBox.Show(tbError.Text);
                }
            }
            catch (Exception ex)
            {
                tbError.AppendText("\r\n");
                tbError.AppendText(ex.Message.ToString());
                //MessageBox.Show("tmrCheckArmSwitch_Tick Error:  " + ex.ToString());
            }
            finally
            {
                // Dispose the grab result if needed for returning it to the grab loop.
                e.DisposeGrabResultIfClone();
            }
        }

        private void OnGrabStarted(Object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                // If called from a different thread, we must use the Invoke method to marshal the call to the proper thread.
                BeginInvoke(new EventHandler<EventArgs>(OnGrabStarted), sender, e);
                return;
            }

            tbError.AppendText("\r\nGrabbing Started. Camera ready for trigger...");
        }

        private void OnGrabStopped(Object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                // If called from a different thread, we must use the Invoke method to marshal the call to the proper thread.
                BeginInvoke(new EventHandler<EventArgs>(OnGrabStopped), sender, e);
                return;
            }

            tbError.AppendText("\r\nGrabbing Stopped");
        }

        private void OnConnectionLost(Object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                // If called from a different thread, we must use the Invoke method to marshal the call to the proper thread.
                BeginInvoke(new EventHandler<EventArgs>(OnConnectionLost), sender, e);
            }

            tbError.AppendText("\r\nCamera has been disconnected. Reconnect and click Initialize Camera.");
            checkBox1.Checked = false;
            checkBox2.Checked = false;
            button2.Enabled = true;
        }

        private void InitializeCam()
        {
            try
            {
                button2.Enabled = false;
 
                cam = new Camera("21824078");
                cam.Open();

                // DeviceVendorName, DeviceModelName, and DeviceFirmwareVersion are string parameters.
                strCamVendor = cam.Parameters[PLCamera.DeviceVendorName].GetValue();
                strCamModel = cam.Parameters[PLCamera.DeviceModelName].GetValue();
                strCamFirmware = cam.Parameters[PLCamera.DeviceFirmwareVersion].GetValue();

                cam.Parameters[PLCamera.UserSetSelector].SetValue(PLCamera.UserSetSelector.Default);
                cam.Parameters[PLCamera.UserSetLoad].Execute();

                cam.Parameters[PLCamera.Width].SetValue(width, IntegerValueCorrection.Nearest);
                cam.Parameters[PLCamera.Height].SetValue(height, IntegerValueCorrection.Nearest);

                cam.Parameters[PLCamera.PixelFormat].SetValue(PLCamera.PixelFormat.Mono8);
                cam.Parameters[PLCamera.GainAuto].TrySetValue(PLCamera.GainAuto.Off);

                if (checkBox3.Checked == true)
                    cam.Parameters[PLCamera.TestImageSelector].SetValue(PLCamera.TestImageSelector.Testimage2);
                else
                    cam.Parameters[PLCamera.TestImageSelector].SetValue(PLCamera.TestImageSelector.Off);

                cam.Parameters[PLCamera.ExposureMode].SetValue(PLCamera.ExposureMode.Timed);
                cam.Parameters[PLCamera.ExposureTimeAbs].SetValue(100.0);
                exposureTime = cam.Parameters[PLCamera.ExposureTimeAbs].GetValue();

                // Configure AcquisitionStart Trigger
                cam.Parameters[PLCamera.AcquisitionMode].SetValue(PLCamera.AcquisitionMode.Continuous);
                cam.Parameters[PLCamera.AcquisitionFrameCount].SetValue(2);
                cam.Parameters[PLCamera.TriggerSelector].SetValue(PLCamera.TriggerSelector.AcquisitionStart);
                cam.Parameters[PLCamera.TriggerSource].SetValue(PLCamera.TriggerSource.Line3);
                cam.Parameters[PLCamera.TriggerMode].SetValue(PLCamera.TriggerMode.On);

                // Configure FrameStart Trigger
                cam.Parameters[PLCamera.TriggerSelector].SetValue(PLCamera.TriggerSelector.FrameStart);
                cam.Parameters[PLCamera.TriggerMode].SetValue(PLCamera.TriggerMode.Off);

                // Configure LineStart Trigger
                cam.Parameters[PLCamera.TriggerSelector].SetValue(PLCamera.TriggerSelector.LineStart);
                cam.Parameters[PLCamera.TriggerSource].SetValue(PLCamera.TriggerSource.ShaftEncoderModuleOut);
                cam.Parameters[PLCamera.TriggerActivation].SetValue(PLCamera.TriggerActivation.RisingEdge);
                cam.Parameters[PLCamera.TriggerMode].SetValue(PLCamera.TriggerMode.Off);

                // Configure Shaft Encoder Software Module
                cam.Parameters[PLCamera.ShaftEncoderModuleLineSelector].SetValue(PLCamera.ShaftEncoderModuleLineSelector.PhaseA);
                cam.Parameters[PLCamera.ShaftEncoderModuleLineSource].SetValue(PLCamera.ShaftEncoderModuleLineSource.Line1);
                cam.Parameters[PLCamera.ShaftEncoderModuleLineSelector].SetValue(PLCamera.ShaftEncoderModuleLineSelector.PhaseB);
                cam.Parameters[PLCamera.ShaftEncoderModuleLineSource].SetValue(PLCamera.ShaftEncoderModuleLineSource.Line2);
                cam.Parameters[PLCamera.ShaftEncoderModuleMode].SetValue(PLCamera.ShaftEncoderModuleMode.AnyDirection);
                cam.Parameters[PLCamera.ShaftEncoderModuleCounterMode].SetValue(PLCamera.ShaftEncoderModuleCounterMode.FollowDirection);
                cam.Parameters[PLCamera.ShaftEncoderModuleCounterMax].SetValue(32000);
                cam.Parameters[PLCamera.ShaftEncoderModuleReverseCounterMax].SetValue(10000);

                // Set up parameters for input lines (termination resistor, debounce, line inverter)
                // Input Line 1
                cam.Parameters[PLCamera.LineSelector].SetValue(PLCamera.LineSelector.Line1);
                cam.Parameters[PLCamera.LineTermination].SetValue(false);
                cam.Parameters[PLCamera.LineInverter].SetValue(false);
                // Input Line 2
                cam.Parameters[PLCamera.LineSelector].SetValue(PLCamera.LineSelector.Line2);
                cam.Parameters[PLCamera.LineTermination].SetValue(false);
                cam.Parameters[PLCamera.LineInverter].SetValue(false);
                // Input Line 3
                cam.Parameters[PLCamera.LineSelector].SetValue(PLCamera.LineSelector.Line3);
                cam.Parameters[PLCamera.LineTermination].SetValue(false);
                cam.Parameters[PLCamera.LineInverter].SetValue(false);

                // Disable Output Lines
                // Disable Output Line 1
                cam.Parameters[PLCamera.LineSelector].SetValue(PLCamera.LineSelector.Out1);
                cam.Parameters[PLCamera.LineSource].SetValue(PLCamera.LineSource.Off);

                // Disable Output Line 2
                cam.Parameters[PLCamera.LineSelector].SetValue(PLCamera.LineSelector.Out2);
                cam.Parameters[PLCamera.LineSource].SetValue(PLCamera.LineSource.Off);

                // Retrieve AOI setting from camera to check if correct
                intAOIOffsetX = cam.Parameters[PLCamera.OffsetX].GetValue();
                intAOIOffsetY = cam.Parameters[PLCamera.OffsetY].GetValue();
                intAOIWidth = cam.Parameters[PLCamera.Width].GetValue();
                intAOIHeight = cam.Parameters[PLCamera.Height].GetValue();

                int lineRate = (int)cam.Parameters[PLCamera.ResultingLineRateAbs].GetValue();
                textBox2.Text = lineRate.ToString();

                // add event handlers for image grabbed and connection lost events
                cam.StreamGrabber.ImageGrabbed += OnImageGrabbed;
                cam.ConnectionLost += OnConnectionLost;
                cam.StreamGrabber.GrabStarted += OnGrabStarted;
                cam.StreamGrabber.GrabStopped += OnGrabStopped;

                // Start checking Camera Arm Switch
                tmrCheckArmSwitch.Tick += new EventHandler(tmrCheckArmSwitch_Tick);
                tmrCheckArmSwitch.Start();

                // Display camera information
                tb1.Text = strCamVendor + " " + strCamModel
                    + "\r\n"
                    + strCamFirmware
                    + "\r\n"
                    + "Width: " + intAOIWidth.ToString()
                    + "\r\n"
                    + "Height: " + intAOIHeight.ToString()
                    + "\r\n"
                    + "OffsetX: " + intAOIOffsetX.ToString()
                    + "\r\n"
                    + "OffsetY: " + intAOIOffsetY.ToString()
                    + "\r\n"
                    + "Exposure Time (us): " + exposureTime.ToString();

                tbError.AppendText("\r\nCamera Initialized.");

                isInitialized = true;

                cam.StreamGrabber.Start(GrabStrategy.OneByOne, GrabLoop.ProvidedByStreamGrabber);

            }
            catch (Exception e)
            {
                tbError.AppendText("\r\n");
                tbError.AppendText(e.Message.ToString());

                button2.Enabled = true;
            }
        }

        private void tmrCheckArmSwitch_Tick(object sender, EventArgs e)
        {
            // This timer is set to 500ms.
            try
            {
                if (isLowered == false)
                {
                    // collect information about the arm state and update the user
                    // Line 3 is OFF (Arm is Up)...
                    lngCamLineStatusAll = cam.Parameters[PLCamera.LineStatusAll].GetValue();
                    textBox1.Text = Convert.ToString(lngCamLineStatusAll, 2);
                    btnArmStatus.BackColor = Color.Green;
                    btnArmStatus.Text = "Arm UP";
                }
                else
                {
                    // Collect information about the arm being lowered and inform the user
                    // Line 3 is ON (Arm is Down)...
                    btnArmStatus.BackColor = Color.Red;
                    btnArmStatus.Text = "Arm DOWN";
                    lngCamLineStatusAll = cam.Parameters[PLCamera.LineStatusAll].GetValue();
                    textBox1.Text = Convert.ToString(lngCamLineStatusAll, 2);
                }

            }
            catch (Exception ex)
            {
                tbError.AppendText("\r\n");
                tbError.AppendText(ex.Message.ToString());
                if (cam.IsConnected == false)
                    tmrCheckArmSwitch.Stop();
            }
        }

        public frmMain()
        {
            InitializeComponent();

            InitializeCam();
        }
    }
}
