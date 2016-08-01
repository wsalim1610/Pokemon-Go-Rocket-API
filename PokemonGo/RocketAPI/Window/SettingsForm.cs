using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using GMap.NET.MapProviders;
using GMap.NET;
using System.Configuration;
using System.Globalization; 
using GMap.NET.WindowsForms;
using PokemonGo.RocketAPI.Helpers;
using GMap.NET.WindowsForms.Markers;


namespace PokemonGo.RocketAPI.Window
{
    partial class SettingsForm : Form
    {
        GMapOverlay searchAreaOverlay = new GMapOverlay("areas");
        GMapOverlay playerOverlay = new GMapOverlay("players");
        GMarkerGoogle playerMarker;

        public SettingsForm()
        {
            InitializeComponent();
        }

        private void SettingsForm_Load(object sender, EventArgs e)
        {

            authTypeCb.Text = Settings.Instance.AuthType.ToString();
            if (authTypeCb.Text == "google")
            {
                UserLoginBox.Text = Settings.Instance.Email.ToString();
                UserPasswordBox.Text = Settings.Instance.Password.ToString();
            } else
            {
                UserLoginBox.Text = Settings.Instance.PtcUsername.ToString();
                UserPasswordBox.Text = Settings.Instance.PtcPassword.ToString();
            }
            latitudeText.Text = Settings.Instance.DefaultLatitude.ToString();
            longitudeText.Text = Settings.Instance.DefaultLongitude.ToString();
            razzmodeCb.Text = Settings.Instance.RazzBerryMode;
            razzSettingText.Text = Settings.Instance.RazzBerrySetting.ToString();
            transferTypeCb.Text = Settings.Instance.TransferType;
            transferCpThresText.Text = Settings.Instance.TransferCPThreshold.ToString();
            transferIVThresText.Text = Settings.Instance.TransferIVThreshold.ToString();
            evolveAllChk.Checked = Settings.Instance.EvolveAllGivenPokemons;
            CatchPokemonBox.Checked = Settings.Instance.CatchPokemon;
            TravelSpeedBox.Text = Settings.Instance.TravelSpeed.ToString();

            // Initialize map:
            //use google provider
            gMapControl1.MapProvider = GoogleMapProvider.Instance;
            //get tiles from server only
            gMapControl1.Manager.Mode = AccessMode.ServerOnly;
            //not use proxy
            GMapProvider.WebProxy = null;
            //center map on moscow
            string lat = ConfigurationManager.AppSettings["DefaultLatitude"];
            string longit = ConfigurationManager.AppSettings["DefaultLongitude"];
            lat.Replace(',', '.');
            longit.Replace(',', '.');
            gMapControl1.Position = new PointLatLng(double.Parse(lat.Replace(",", "."), CultureInfo.InvariantCulture), double.Parse(longit.Replace(",", "."), CultureInfo.InvariantCulture));
            
            //zoom min/max; default both = 2
            gMapControl1.DragButton = MouseButtons.Left;
            gMapControl1.IgnoreMarkerOnMouseWheel = true;

            gMapControl1.CenterPen = new Pen(Color.Red, 2);
            gMapControl1.MinZoom = trackBar.Maximum = 1;
            gMapControl1.MaxZoom = trackBar.Maximum = 20;
            trackBar.Value = 10;

            gMapControl1.Overlays.Add(searchAreaOverlay);
            gMapControl1.Overlays.Add(playerOverlay);
            
            playerMarker = new GMarkerGoogle(gMapControl1.Position, GMarkerGoogleType.orange_small);
            playerOverlay.Markers.Add(playerMarker);
            
            S2GMapDrawer.DrawS2Cells(S2Helper.GetNearbyCellIds(gMapControl1.Position.Lng, gMapControl1.Position.Lat), searchAreaOverlay);

            //set zoom
            gMapControl1.Zoom = trackBar.Value;
        }

        private void saveBtn_Click(object sender, EventArgs e)
        {
            Settings.Instance.SetSetting(authTypeCb.Text, "AuthType");
            if (authTypeCb.Text == "google")
            {
                Settings.Instance.SetSetting(UserLoginBox.Text, "Email");
                Settings.Instance.SetSetting(UserPasswordBox.Text, "Password");
            } else
            {
                Settings.Instance.SetSetting(UserLoginBox.Text, "PtcUsername");
                Settings.Instance.SetSetting(UserPasswordBox.Text, "PtcPassword");
            }
            Settings.Instance.SetSetting(latitudeText.Text.Replace(',', '.'), "DefaultLatitude");
            Settings.Instance.SetSetting(longitudeText.Text.Replace(',', '.'), "DefaultLongitude");

            string lat = ConfigurationManager.AppSettings["DefaultLatitude"];
            string longit = ConfigurationManager.AppSettings["DefaultLongitude"];
            lat.Replace(',', '.');
            longit.Replace(',', '.');


            Settings.Instance.SetSetting(razzmodeCb.Text, "RazzBerryMode");
            Settings.Instance.SetSetting(razzSettingText.Text, "RazzBerrySetting");
            Settings.Instance.SetSetting(transferTypeCb.Text, "TransferType");
            Settings.Instance.SetSetting(transferCpThresText.Text, "TransferCPThreshold");
            Settings.Instance.SetSetting(transferIVThresText.Text, "TransferIVThreshold");
            Settings.Instance.SetSetting(TravelSpeedBox.Text, "TravelSpeed");
            Settings.Instance.SetSetting(evolveAllChk.Checked ? "true" : "false", "EvolveAllGivenPokemons");
            Settings.Instance.SetSetting(CatchPokemonBox.Checked ? "true" : "false", "CatchPokemon");
            Settings.Instance.Reload();

            MainForm.Instance.RestartBot();

            Close();
        }

        private void authTypeCb_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (authTypeCb.Text == "google")
            {
                UserLabel.Text = "Email:";
            }
            else
            {
                UserLabel.Text = "Username:";
            }
        }

        private void gMapControl1_MouseClick(object sender, MouseEventArgs e)
        {
            if (gMapControl1.IsDragging) return;
            if (e.Button == MouseButtons.Left)
            {
                Point localCoordinates = e.Location;
                PointLatLng clickedCoord = gMapControl1.FromLocalToLatLng(localCoordinates.X, localCoordinates.Y);
                
                if (e.Clicks == 1)
                {
                    double X = Math.Round(clickedCoord.Lng, 6);
                    double Y = Math.Round(clickedCoord.Lat, 6);
                    string longitude = X.ToString();
                    string latitude = Y.ToString();
                    latitudeText.Text = latitude;
                    longitudeText.Text = longitude;

                    moveTo(clickedCoord);
                }
            }
        }

        private void latitudeText_TextChanged(object sender, EventArgs e)
        {
            if (playerMarker == null) return;

            double newLat;
            if (double.TryParse(latitudeText.Text, out newLat))
            {
                moveTo(new PointLatLng(Math.Round(newLat, 6), playerMarker.Position.Lng), true);
            }
            //latitudeText.Text = Math.Round(playerMarker.Position.Lng, 6).ToString();
        }

        private void longitudeText_TextChanged(object sender, EventArgs e)
        {
            if (playerMarker == null) return;

            double newLng;
            if (double.TryParse(longitudeText.Text, out newLng))
            {
                moveTo(new PointLatLng(playerMarker.Position.Lat, Math.Round(newLng, 6)), true);
            }
            //latitudeText.Text = Math.Round(playerMarker.Position.Lat, 6).ToString();
        }

        private void moveTo(PointLatLng pos, bool shouldFocus = false)
        {
            if (shouldFocus)
            {
                gMapControl1.Position = pos;
            }
            playerMarker.Position = pos;

            searchAreaOverlay.Polygons.Clear();
            S2GMapDrawer.DrawS2Cells(S2Helper.GetNearbyCellIds(pos.Lng, pos.Lat), searchAreaOverlay);
        }

        private void gMapControl1_OnMapZoomChanged()
        {
            trackBar.Value = (int)gMapControl1.Zoom;
        }

        private void gMapControl1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            PointLatLng clickedCoord = gMapControl1.FromLocalToLatLng(e.Location.X, e.Location.Y);
            gMapControl1.Position = clickedCoord;
            gMapControl1.Zoom += 5;
        }

        private void trackBar_Scroll(object sender, EventArgs e)
        {
            gMapControl1.Zoom = trackBar.Value;
        }

        private void FindAdressButton_Click(object sender, EventArgs e)
        {
            gMapControl1.SetPositionByKeywords(AdressBox.Text);
            gMapControl1.Zoom = 15;
        }

        private void transferTypeCb_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (transferTypeCb.Text == "CP")
            {
                label4.Visible = true;
                transferCpThresText.Visible = true;
            }
            else
            {
                label4.Visible = false;
                transferCpThresText.Visible = false;

            }

            if (transferTypeCb.Text == "IV")
            {
                label6.Visible = true;
                transferIVThresText.Visible = true;
            }
            else
            {
                label6.Visible = false;
                transferIVThresText.Visible = false;

            }

        }

        private void FindAdressButton_Click_1(object sender, EventArgs e)
        {
            gMapControl1.SetPositionByKeywords(AdressBox.Text);
            gMapControl1.Zoom = 15;
            double X = Math.Round(gMapControl1.Position.Lng, 6);
            double Y = Math.Round(gMapControl1.Position.Lat, 6);
            string longitude = X.ToString();
            string latitude = Y.ToString();
            latitudeText.Text = latitude;
            longitudeText.Text = longitude;

            moveTo(gMapControl1.Position, true);
        }
    }
}