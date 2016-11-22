//Author: Bastian Wieck
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;
using PacketDotNet;
using SharpPcap;
using Excel = Microsoft.Office.Interop.Excel;
using System.IO;
using System.Xml;
using GMap.NET;
using NetFwTypeLib;
using System.Runtime.InteropServices;


namespace MyPacketCapturer
{
    public partial class frmCapture : Form
    {
        //List that stores the results from the IP locator website, as well as the count for each location
        private static List<LocatorCounter> locations = new List<LocatorCounter> { };

        //All markers
        private static GMap.NET.WindowsForms.GMapOverlay markers = new GMap.NET.WindowsForms.GMapOverlay("markers");

        CaptureDeviceList devices;  //List of devices for this computers
        public static ICaptureDevice device;  //the device we will be using
        public static string stringPackets = "";  //data that was captured
        static int numPackets = 0;//number of packets captured
        frmSend fSend; //This will be our send form
        public static int intranetwork = 0;//counter for intranetwork traffic
        
        //coordinates used to set the zoom level and to center the map
        static double maxLat = 0;
        static double minLat = 0;
        static double maxLong = 0;
        static double minLong = 0;

        //string to be placed in txtLocations
        private static string counter = "";

        public frmCapture()
        {
            InitializeComponent();

            //Bing Map is supposed to be the fastest Map Provider
            gMap.MapProvider = GMap.NET.MapProviders.BingMapProvider.Instance;
          
            GMap.NET.GMaps.Instance.Mode = GMap.NET.AccessMode.ServerOnly;

            //get the list of devices
            devices = CaptureDeviceList.Instance;

            //make sure that there is at least one device
            if (devices.Count < 1)
            {
                MessageBox.Show("no Capture Devices Found!");
                Application.Exit();
            }

            //add devices to the combo box
            foreach (ICaptureDevice dev in devices)
            {
                cmbDevices.Items.Add(dev.Description);
            }

            //get the third device and display in combo box
            device = devices[0];
            cmbDevices.Text = device.Description;

            //register our handler function to the packet arrival event
            device.OnPacketArrival += new SharpPcap.PacketArrivalEventHandler(device_OnPacketArrival);

            int readTimeoutMilliseconds = 1000;
            device.Open(DeviceMode.Promiscuous, readTimeoutMilliseconds);
        }

        private static void device_OnPacketArrival(object sender, CaptureEventArgs packet)
        {
            //it skips random packet number
            //increment number of packets captured
            numPackets++;

            //put the packet number in the capture window
            stringPackets += "Packet Number: " + Convert.ToString(numPackets);
            stringPackets += Environment.NewLine;

            //array to store our data
            byte[] data = packet.Packet.Data;

            //keep track of the number of bytes displayed per line
            int byteCounter = 0;
            String senderIPStr = "";
            String senderIPARP = "";
            String senderIPIP = "";
            bool ip = false;
            bool arp = false;
            bool routetable = false;
            stringPackets += "Destination MAC Address: ";
            //parsing the packets 
            foreach (byte b in data)
            {

                //add the byte to our string in hexadecimal
                if (byteCounter <= 13) stringPackets += b.ToString("X2") + " ";
                //gets the IP Address for ARP and IP packets
                if (byteCounter >= 28 && byteCounter <= 31) senderIPARP += b.ToString("X2");
                if (byteCounter >= 26 && byteCounter <= 29) senderIPIP += b.ToString("X2");
                byteCounter++;
                switch (byteCounter)
                {
                    case 6: stringPackets += Environment.NewLine;
                        stringPackets += "Source MAC Address: ";
                        break;
                    case 12: stringPackets += Environment.NewLine;
                        stringPackets += "EtherType: ";
                        break;
                    case 14: if (data[12] == 8)
                        {
                            if (data[13] == 0)
                            {
                                stringPackets += "(IP)";
                                ip = true;
                            }
                            if (data[13] == 6)
                            {
                                stringPackets += "(ARP)";
                                arp = true;
                            }

                        }
                        break;
                    case 27:
                        if (arp || ip)
                        {
                            stringPackets += Environment.NewLine;
                            stringPackets += "Sender IP Address: ";
                        }

                        break;
                }

            }
            if (ip) senderIPStr = senderIPIP;
            if (arp) senderIPStr = senderIPARP;

            //convert to dotted decimal, because the website I'm using needs this format
            String dottedIP = "";
            for (int i = 1; i < senderIPStr.Length; i += 2)
            {
                String hexIP = senderIPStr.Substring(i - 1, 2);
                int decValue = int.Parse(hexIP, System.Globalization.NumberStyles.HexNumber);
                if (i < senderIPStr.Length - 1) dottedIP += decValue + ".";
                else dottedIP += decValue;
            }

            StringBuilder output = new StringBuilder();
            //check if it's an IP or ARP 
            if (arp || ip)
            {
                //adds the IP Address
                stringPackets += dottedIP;
                stringPackets += Environment.NewLine;

                //if the first number in IP is 10, if first two numbers are between 172.16 and less than 172.32 or when it start with 192.168 it is intranetwork traffic
                int ind = dottedIP.IndexOf('.');
                int ind2 = dottedIP.IndexOf('.', ind + 1);
                if(dottedIP.Substring(0,ind)=="10"||(dottedIP.Substring(0,ind)=="172"&&Convert.ToInt32(dottedIP.Substring(ind+1,ind2-ind-1))>=16&&Convert.ToInt32(dottedIP.Substring(ind+1,ind2-ind-1))<32)
                    || dottedIP.Substring(0, ind2) == "192.168")
                {
                    stringPackets += "Intranetwork traffic";
                    intranetwork++;

                }
                else
                {
                    //calls the method to get the location
                    DataTable table = getLocation(dottedIP);

                    //convert the DataTable into string
                    foreach (DataRow dataRow in table.Rows)
                    {
                        int r = 0;
                        foreach (DataColumn dataColum in table.Columns)
                        {
                            //skips the first colum, because it contains the already printed IP address
                            if (r != 0) output.AppendFormat("{0} ", dataRow[dataColum]);
                            r++;
                        }
                        output.AppendLine();
                    }
                    //if output is three 0's it's a special address and not routetable
                    if (output.ToString().Contains("0 0 0 ")) stringPackets += "Special Address. Not Routetable." + Environment.NewLine;
                    else
                    {
                        stringPackets += output;
                        routetable = true;
                    }
                }
                

            }

            //check if there's is a location
            if (routetable)
            {
                LocatorCounter lc = null;
                Boolean first = true;
                //check if it's a new location
                foreach (var x in locations)
                {
                    if (x.getLocation().Equals(output)) //marker already exists
                    {
                        lc = x;
                        first = false;
                        break;
                    }
                }
                if (first) //new marker
                {
                    lc = new LocatorCounter(output);
                    locations.Add(lc);
                    showLocation(output);
                }
                else lc.increase(); //increase the count for a location

                //text box that displays the number of packets at each location
                counter = "";
                foreach (var x in locations)
                {
                    counter += "Packetcount: " + x.getCount() + " at Location: " + x.getLocation();
                }

            }


            stringPackets += Environment.NewLine + Environment.NewLine;

            byteCounter = 0;
            stringPackets += "Raw Data" + Environment.NewLine;

            //process each byte in our captured packet
            foreach (byte b in data)
            {
                //add the byte to our string in hexadecimal
                stringPackets += b.ToString("X2") + " ";
                byteCounter++;

                if (byteCounter == 16)
                {
                    byteCounter = 0;
                    stringPackets += Environment.NewLine;
                }

            }
            stringPackets += Environment.NewLine;
            stringPackets += Environment.NewLine;
        }


        private void btnStartStop_Click(object sender, EventArgs e)
        {
            try
            {
                if (btnStartStop.Text == "Start")
                {
                    device.StartCapture();
                    timer1.Enabled = true;
                    btnStartStop.Text = "Stop";
                }
                else
                {   
                    //change the text before the capturing is stopped
                    btnStartStop.Text = "Start";
                    device.StopCapture();
                    timer1.Enabled = false;
                }
            }
            catch
            {

            }
        }

        //dump the packet data from stringPackets to the text box
        private void timer1_Tick(object sender, EventArgs e)
        {
            txtLocations.Text = "Packetcount: " + intranetwork + " from private IP's" + Environment.NewLine + counter;
            txtCapturedData.AppendText(stringPackets);
            stringPackets = "";
            txtNumPackets.Text = Convert.ToString(numPackets);

        }

        private void cmbDevices_SelectedIndexChanged(object sender, EventArgs e)
        {
            device = devices[cmbDevices.SelectedIndex];
            cmbDevices.Text = device.Description;
            txtGUID.Text = device.Name;

            //register our handler function to the packet arrival event
            device.OnPacketArrival += new SharpPcap.PacketArrivalEventHandler(device_OnPacketArrival);

            int readTimeoutMilliseconds = 1000;
            device.Open(DeviceMode.Promiscuous, readTimeoutMilliseconds);
        }

        private void saveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            saveFileDialog1.Filter = "Text Files|*.txt|All Files|*.*";
            saveFileDialog1.Title = "Save the Captured Packets";
            saveFileDialog1.ShowDialog();

            //Check to see if filename was given
            if (saveFileDialog1.FileName != "")
            {
                System.IO.File.WriteAllText(saveFileDialog1.FileName, txtCapturedData.Text);
            }
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            openFileDialog1.Filter = "Text Files|*.txt|All Files|*.*";
            openFileDialog1.Title = "open the Captured Packets";
            openFileDialog1.ShowDialog();

            //Check to see if filename was given
            if (openFileDialog1.FileName != "")
            {
                txtCapturedData.Text = System.IO.File.ReadAllText(openFileDialog1.FileName);
            }
        }

        private void sendWindowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (frmSend.instantiations == 0)
            {
                fSend = new frmSend(); // creates a new frmSend
                fSend.Show();

            }
        }

        //method to display the coordinates on google maps
        private static void showLocation(StringBuilder s)
        {
            try
            {
                gMap.Overlays.Clear();
                //getting the coordinates
                String[] strings = s.ToString().Split(new char[] { ' ' });
                int length = strings.Length;
                double latitude = Double.Parse(strings[length - 4]);
                double longitude = Double.Parse(strings[length - 3]);

                if (markers.Markers.Count > 0)
                {   //starting at the second marker find maximum and minumum longitude and latitude
                    if (latitude > maxLat) maxLat = latitude;
                    else if (latitude < minLat) minLat = latitude;
                    if (longitude > maxLong) maxLong = longitude;
                    else if (longitude < minLong) minLong = longitude;
                }
                else
                {   // first marker
                    maxLat = latitude+1;
                    maxLong = longitude+1;
                    minLat = latitude-1;
                    minLong = longitude-1;
                }
                

                //creating a marker
                GMap.NET.WindowsForms.GMapMarker marker =
                    new GMap.NET.WindowsForms.Markers.GMarkerGoogle(
                    new GMap.NET.PointLatLng(latitude, longitude),
                    GMap.NET.WindowsForms.Markers.GMarkerGoogleType.red_small);
                gMap.Overlays.Add(markers);
                marker.ToolTipText = s.ToString();//when going over a marker it shows the location
                markers.Markers.Add(marker);//add to the list
                double mid1 = (maxLat - minLat)/2+minLat;
                double mid2 = (maxLong - minLong)/2+minLong;
                double dist1 = maxLong - minLong;
                double dist2 = maxLat - minLat;
                

                //The zoom and center gave me trouble, that's why all the try's and catch's are here
                try
                {   //center the map in the middle of all markers using the middle coordinates
                    gMap.SetPositionByKeywords(mid1 + " " + mid2);
                }
                catch
                {

                }
                try
                {
                    //zoom all the way out before adjusting zoom
                    gMap.Zoom = 2;
                }
                catch
                {

                }
               while(gMap.ViewArea.WidthLng > dist1 && gMap.ViewArea.HeightLat > dist2 && gMap.Zoom <= 15)
                    {
                        //while the markers are visible zoom in
                        increaseZoom();

                    }
                //the zoom in the loop is going to be one zoom in too much
                //zoom out one, so that all the markers are visible. The if statement is there, because 2 is the min zoom
               if(gMap.Zoom>2)gMap.Zoom--;
                
               //setArea(r2);
                              
                
            }
            catch
            {
                
            }

        }

        //method to increase zoom
        private static void increaseZoom(){
            try{
                gMap.Zoom++;
            }
            catch{

            }
        }
        
        //The locator method. Gets the location using a website
        //code for this method is copied 
        private static DataTable getLocation(string varIPAddress)
        {
            WebRequest varWebRequest = WebRequest.Create("http://freegeoip.net/xml/" + varIPAddress);
            WebProxy px = new WebProxy("http://freegeoip.net/xml/" + varIPAddress, true);
            varWebRequest.Proxy = px;
            varWebRequest.Timeout = 2000;
            try
            {
                WebResponse rep = varWebRequest.GetResponse();
                XmlTextReader xtr = new XmlTextReader(rep.GetResponseStream());
                DataSet ds = new DataSet();
                ds.ReadXml(xtr);
                return ds.Tables[0];
            }
            catch
            {
                return null;
            }
        }
    }//end of frmCapture class

        //class to keep track of the nr of packets at each location
       partial class LocatorCounter
        {
            private int count = 0;
            private StringBuilder location;
            public LocatorCounter(StringBuilder location)
            {
                this.location = location;
                this.count++;
            }
            public void increase()
            {
                this.count++;
            }
            public int getCount()
            {
                return this.count;
            }
            public StringBuilder getLocation()
            {
                return this.location;
            }
        }
   }
