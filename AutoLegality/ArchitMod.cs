using System;
using System.Linq;
using System.Media;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Reflection;

using PKHeX.Core;
using PKHeX.WinForms.Properties;
using PKHeX.WinForms.AutoLegality;

namespace PKHeX.WinForms.Controls
{
    public partial class PKMEditor : UserControl
    {

        /// <summary>
        /// Helper function to print out a byte array as a string that can be used within code
        /// </summary>
        /// <param name="bytes">byte array</param>
        public void PrintByteArray(byte[] bytes)
        {
            var sb = new System.Text.StringBuilder("new byte[] { ");
            foreach (var b in bytes)
            {
                sb.Append(b + ", ");
            }
            sb.Append("}");
            Console.WriteLine(sb.ToString());
        }


        /// <summary>
        /// Accessible method to lead Fields from PKM
        /// The Loading is done in WinForms
        /// </summary>
        /// <param name="pk">The PKM file</param>
        /// <param name="focus">Set input focus to control</param>
        /// <param name="skipConversionCheck">Default this to true</param>
        public void LoadFieldsFromPKM2(PKM pk, bool focus = true, bool skipConversionCheck = true)
        {
            if (pk == null) { WinFormsUtil.Error("Attempted to load a null file."); return; }
            if (focus)
                Tab_Main.Focus();

            if (!skipConversionCheck && !PKMConverter.TryMakePKMCompatible(pk, CurrentPKM, out string c, out pk))
            { WinFormsUtil.Alert(c); return; }

            bool oldInit = FieldsInitialized;
            FieldsInitialized = FieldsLoaded = false;

            pkm = pk.Clone();

            try { GetFieldsfromPKM(); }
            finally { FieldsInitialized = oldInit; }

            Stats.UpdateIVs(null, null);
            UpdatePKRSInfected(null, null);
            UpdatePKRSCured(null, null);

            if (HaX) // Load original values from pk not pkm
            {
                MT_Level.Text = (pk.Stat_HPMax != 0 ? pk.Stat_Level : PKX.GetLevel(pk.EXP, pk.Species, pk.AltForm)).ToString();
                TB_EXP.Text = pk.EXP.ToString();
                MT_Form.Text = pk.AltForm.ToString();
                if (pk.Stat_HPMax != 0) // stats present
                {
                    Stats.LoadPartyStats(pk);
                }
            }
            FieldsLoaded = true;

            SetMarkings();
            UpdateLegality();
            UpdateSprite();
            LastData = PreparePKM()?.Data;
        }

        /// <summary>
        /// Load PKM from byte array. The output is a boolean, which is true if a byte array is loaded into WinForms, else false
        /// </summary>
        /// <param name="input">Byte array input correlating to the PKM</param>
        /// <param name="path">Path to the file itself</param>
        /// <param name="ext">Extension of the file</param>
        /// <param name="SAV">Type of save file</param>
        /// <returns></returns>
        private bool TryLoadPKM(byte[] input, string path, string ext, SaveFile SAV)
        {
            var temp = PKMConverter.GetPKMfromBytes(input, prefer: ext.Length > 0 ? (ext.Last() - 0x30) & 7 : SAV.Generation);
            if (temp == null)
                return false;

            var type = CurrentPKM.GetType();
            PKM pk = PKMConverter.ConvertToType(temp, type, out string c);
            if (pk == null)
            {
                return false;
            }
            if (SAV.Generation < 3 && ((pk as PK1)?.Japanese ?? ((PK2)pk).Japanese) != SAV.Japanese)
            {
                var strs = new[] { "International", "Japanese" };
                var val = SAV.Japanese ? 0 : 1;
                WinFormsUtil.Alert($"Cannot load {strs[val]} {pk.GetType().Name}s to {strs[val ^ 1]} saves.");
                return false;
            }

            PopulateFields(pk);
            Console.WriteLine(c);
            return true;
        }

        /// <summary>
        /// Set Country, SubRegion and Console in WinForms
        /// </summary>
        /// <param name="Country">String denoting the exact country</param>
        /// <param name="SubRegion">String denoting the exact sub region</param>
        /// <param name="ConsoleRegion">String denoting the exact console region</param>
        public void SetRegions(string Country, string SubRegion, string ConsoleRegion, PKM pk)
        {
            pk.Country = Util.GetCBList("countries", "en").FirstOrDefault(z => z.Text == Country).Value;
            pk.Region = Util.GetCBList($"sr_{pk.Country:000}", "en").FirstOrDefault(z => z.Text == SubRegion).Value;
            pk.ConsoleRegion = Util.GetUnsortedCBList("regions3ds").FirstOrDefault(z => z.Text == ConsoleRegion).Value;
        }

        /// <summary>
        /// Set Country, SubRegion and ConsoleRegion in a PKM directly
        /// </summary>
        /// <param name="Country">INT value corresponding to the index of the Country</param>
        /// <param name="SubRegion">INT value corresponding to the index of the sub region</param>
        /// <param name="ConsoleRegion">INT value corresponding to the index of the console region</param>
        /// <param name="pk"></param>
        /// <returns></returns>
        public PKM SetPKMRegions(int Country, int SubRegion, int ConsoleRegion, PKM pk)
        {
            pk.Country = Country;
            pk.Region = SubRegion;
            pk.ConsoleRegion = ConsoleRegion;
            return pk;
        }

        /// <summary>
        /// Set TID, SID and OT
        /// </summary>
        /// <param name="OT">string value of OT name</param>
        /// <param name="TID">INT value of TID</param>
        /// <param name="SID">INT value of SID</param>
        /// <param name="pk"></param>
        /// <returns></returns>
        public PKM SetTrainerData(string OT, int TID, int SID, int gender, PKM pk, bool APILegalized = false)
        {
            if (APILegalized)
            {
                if ((pk.TID == 12345 && pk.OT_Name == "PKHeX") || (pk.TID == 34567 && pk.SID == 0 && pk.OT_Name == "TCD"))
                {
                    bool Shiny = pk.IsShiny;
                    pk.TID = TID;
                    pk.SID = SID;
                    pk.OT_Name = OT;
                    pk.OT_Gender = gender;
                    AutoLegalityModMain.AutoLegalityMod.SetShinyBoolean(pk, Shiny);
                }
                return pk;
            }
            pk.TID = TID;
            pk.SID = SID;
            pk.OT_Name = OT;
            return pk;
        }

        /// <summary>
        /// Check the mode for trainerdata.json
        /// </summary>
        /// <param name="jsonstring">string form of trainerdata.json</param>
        /// <returns></returns>
        public string checkMode(string jsonstring = "")
        {
            if(jsonstring != "")
            {
                string mode = "save";
                if (jsonstring.Contains("mode")) mode = jsonstring.Split(new string[] { "\"mode\"" }, StringSplitOptions.None)[1].Split('"')[1].ToLower();
                if (mode != "game" && mode != "save" && mode != "auto") mode = "save"; // User Mistake or for some reason this exists as a value of some other key
                return mode;
            }
            else
            {
                if (!System.IO.File.Exists(System.IO.Path.GetDirectoryName(Application.ExecutablePath) + "\\trainerdata.json"))
                {
                    return "save"; // Default trainerdata.txt handling
                }
                jsonstring = System.IO.File.ReadAllText(System.IO.Path.GetDirectoryName(Application.ExecutablePath) + "\\trainerdata.json", System.Text.Encoding.UTF8);
                if (jsonstring != "") return checkMode(jsonstring);
                else
                {
                    WinFormsUtil.Alert("Empty trainerdata.json file");
                    return "save";
                }
            }
        }

        /// <summary>
        /// check if the game exists in the json file. If not handle via trainerdata.txt method
        /// </summary>
        /// <param name="jsonstring">Complete trainerdata.json string</param>
        /// <param name="Game">int value of the game</param>
        /// <param name="jsonvalue">internal json: trainerdata[Game]</param>
        /// <returns></returns>
        public bool checkIfGameExists(string jsonstring, int Game, out string jsonvalue)
        {
            jsonvalue = "";
            if (checkMode(jsonstring) == "auto")
            {
                jsonvalue = "auto";
                return false;
            }
            if (!jsonstring.Contains("\"" + Game.ToString() + "\"")) return false;
            foreach (string s in jsonstring.Split(new string[] { "\"" + Game.ToString() + "\"" }, StringSplitOptions.None))
            {
                if (s.Trim()[0] == ':')
                {
                    int index = jsonstring.IndexOf(s);
                    jsonvalue = jsonstring.Substring(index).Split('{')[1].Split('}')[0].Trim();
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// String parse key to find value from final json
        /// </summary>
        /// <param name="key"></param>
        /// <param name="finaljson"></param>
        /// <returns></returns>
        public string getValueFromKey(string key, string finaljson)
        {
            return finaljson.Split(new string[] { key }, StringSplitOptions.None)[1].Split('"')[2].Trim();
        }

        /// <summary>
        /// Convert TID7 and SID7 back to the conventional TID, SID
        /// </summary>
        /// <param name="tid7">TID7 value</param>
        /// <param name="sid7">SID7 value</param>
        /// <returns></returns>
        public int[] ConvertTIDSID7toTIDSID(int tid7, int sid7)
        {
            var repack = (long)sid7 * 1_000_000 + tid7;
            int sid = (ushort)(repack >> 16);
            int tid = (ushort)repack;
            return new int[] { tid, sid };
        }

        /// <summary>
        /// Function to extract trainerdata values from trainerdata.json
        /// </summary>
        /// <param name="C_SAV">Current Save Editor</param>
        /// <param name="Game">optional Game value in case of mode being game</param>
        /// <returns></returns>
        public string[] parseTrainerJSON(SAVEditor C_SAV, int Game = -1)
        {
            if (!System.IO.File.Exists(System.IO.Path.GetDirectoryName(Application.ExecutablePath) + "\\trainerdata.json"))
            {
                return parseTrainerData(C_SAV); // Default trainerdata.txt handling
            }
            string jsonstring = System.IO.File.ReadAllText(System.IO.Path.GetDirectoryName(Application.ExecutablePath) + "\\trainerdata.json", System.Text.Encoding.UTF8);
            if (Game == -1) Game = C_SAV.SAV.Game; 
            if(!checkIfGameExists(jsonstring, Game, out string finaljson)) return parseTrainerData(C_SAV, finaljson == "auto");
            string TID = getValueFromKey("TID", finaljson);
            string SID = getValueFromKey("SID", finaljson);
            string OT = getValueFromKey("OT", finaljson);
            string Gender = getValueFromKey("Gender", finaljson);
            string Country = getValueFromKey("Country", finaljson);
            string SubRegion = getValueFromKey("SubRegion", finaljson);
            string ConsoleRegion = getValueFromKey("3DSRegion", finaljson);
            if (TID.Length == 6 && SID.Length == 4)
            {
                if(new List<int> { 33, 32, 31, 30 }.IndexOf(Game) == -1) WinFormsUtil.Alert("Force Converting G7TID/G7SID to TID/SID");
                int[] tidsid = ConvertTIDSID7toTIDSID(int.Parse(TID), int.Parse(SID));
                TID = tidsid[0].ToString();
                SID = tidsid[1].ToString();
            }
            return new string[] { TID, SID, OT, Gender, Country, SubRegion, ConsoleRegion };
        }

        /// <summary>
        /// Parser for auto and preset trainerdata.txt files
        /// </summary>
        /// <param name="C_SAV">SAVEditor of the current save file</param>
        /// <returns></returns>
        public string[] parseTrainerData(SAVEditor C_SAV, bool auto = false)
        {
            // Defaults
            string TID = "23456";
            string SID = "34567";
            string OT = "Archit";
            string Gender = "M";
            string Country = "Canada";
            string SubRegion = "Alberta";
            string ConsoleRegion = "Americas (NA/SA)";
            if (!System.IO.File.Exists(System.IO.Path.GetDirectoryName(Application.ExecutablePath) + "\\trainerdata.txt"))
            {
                return new string[] { TID, SID, OT, Gender, Country, SubRegion, ConsoleRegion}; // Default No trainerdata.txt handling
            }
            string[] trainerdataLines = System.IO.File.ReadAllText(System.IO.Path.GetDirectoryName(Application.ExecutablePath) + "\\trainerdata.txt", System.Text.Encoding.UTF8)
                .Split(new string[] { Environment.NewLine }, StringSplitOptions.None);
            List<string> lstlines = trainerdataLines.OfType<string>().ToList();
            int count = lstlines.Count;
            for (int i =0; i < count; i++)
            {
                string item = lstlines[0];
                if (item.TrimEnd() == "" || item.TrimEnd() == "auto") continue;
                string key = item.Split(':')[0].TrimEnd();
                string value = item.Split(':')[1].TrimEnd();
                lstlines.RemoveAt(0);
                if (key == "TID") TID = value;
                else if (key == "SID") SID = value;
                else if (key == "OT") OT = value;
                else if (key == "Gender") Gender = value;
                else if (key == "Country") Country = value;
                else if (key == "SubRegion") SubRegion = value;
                else if (key == "3DSRegion") ConsoleRegion = value;
                else continue;
            }
            // Automatic loading
            if (trainerdataLines[0] == "auto" || auto)
            {
                try
                {
                    int ct = PKMConverter.Country;
                    int sr = PKMConverter.Region;
                    int cr = PKMConverter.ConsoleRegion;
                    if (C_SAV.SAV.Gender == 1) Gender = "F";
                    return new string[] { C_SAV.SAV.TID.ToString("00000"), C_SAV.SAV.SID.ToString("00000"),
                                          C_SAV.SAV.OT, Gender, ct.ToString(),
                                          sr.ToString(), cr.ToString()};
                }
                catch
                {
                    return new string[] { TID, SID, OT, Gender, Country, SubRegion, ConsoleRegion };
                }
            }
            if (TID.Length == 6 && SID.Length == 4)
            {
                int[] tidsid = ConvertTIDSID7toTIDSID(int.Parse(TID), int.Parse(SID));
                TID = tidsid[0].ToString();
                SID = tidsid[1].ToString();
            }
            return new string[] { TID, SID, OT, Gender, Country, SubRegion, ConsoleRegion };
        }

    }

}
