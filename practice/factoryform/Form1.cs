﻿using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace factoryform
{
    public partial class Form1 : Form
    {
        private MySqlConnection connection;
        private bool dbConnectionResult = false;
        private string tempProductName;

        private byte[] rxBuf = new byte[255];
        private int rxLen = 0;
        private Regex regex = new Regex(@"([^\[\], ])+");
        private List<string> messageSubs = new List<string>();
        private List<Label> controlLabels = new List<Label>();
        private List<PictureBox> controlImages = new List<PictureBox>();

        public Form1()
        {
            InitializeComponent();
            DBConnection("127.0.0.1", "factorydb", "root", "mysql18");
            dbConnectionResult = DBOpen();
            if (dbConnectionResult)
                MessageBox.Show("DB open complete");
            else
                MessageBox.Show("DB open failed");
            DBClose();

            serialDevice.DataReceived += new System.IO.Ports.SerialDataReceivedEventHandler(SerialPortDataReceived);
        }

        private bool DBOpen()
        {
            try
            {
                connection.Open();
                return true;
            }
            catch (MySqlException ex)
            {
                switch (ex.Number)
                {
                    case 0:
                        break;
                    case 1045:
                        break;
                }
                return false;
            }
        }

        private bool DBClose()
        {
            try
            {
                connection.Close();
                return true;
            }
            catch (MySqlException ex)
            {
                return false;
            }
        }

        private bool Sql(string strSql)
        {
            DBOpen();
            try
            {
                MySqlCommand cmd = new MySqlCommand(strSql, connection);
                cmd.ExecuteNonQuery();
                DBClose();
                return true;
            }
            catch (Exception ex)
            {
                DBClose();
                return false;
            }
        }

        private void SqlSelect(string strSqlQuery, DataGridView datagv)
        {
            if (DBOpen() == true)
            {
                MySqlCommand cmd = new MySqlCommand(strSqlQuery, connection);
                MySqlDataAdapter da = new MySqlDataAdapter(cmd);

                DataTable dt = new DataTable();
                da.Fill(dt);
                datagv.DataSource = dt;

                DBClose();
            }
            return;
        }

        private string SqlGet(string strSql)
        {
            string result = "";

            DBOpen();
            try
            {
                MySqlCommand cmd = new MySqlCommand(strSql, connection);
                result = cmd.ExecuteScalar().ToString();
                DBClose();
            }
            catch (Exception ex)
            {
                DBClose();
            }
            Console.WriteLine($"queryscaler result: {result}");
            return result;
        }

        private void SerialPortDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            Invoke(new EventHandler(SerialDataGet));
        }

        private void SerialDataGet(object sender, EventArgs e)
        {
            if (serialDevice.IsOpen == false)
                return;

            int bufferCount = serialDevice.BytesToRead;
            for (int i = 0; i < bufferCount; ++i)
            {
                byte data = (byte)serialDevice.ReadByte();
                if (ReceiveDataPacket(data))
                {
                    // checksum
                    if (Checksum(rxBuf))
                        DeviceProcess();

                }
            }
        }

        private bool Checksum(byte[] buf)
        {
            messageSubs.Clear();

            string msg = Encoding.Default.GetString(buf, 0, rxLen);
            lbRecvPacket.Text = msg;
            MatchCollection mc = regex.Matches(msg);
            foreach (Match m in mc)
            {
                Console.WriteLine($"{m.Index}:{m.Value}");
                messageSubs.Add(m.Value);
            }

            Array.Clear(rxBuf, 0, rxLen);
            rxLen = 0;

            // 들어온 값이 8이 아니면 잘못된 패킷
            if (messageSubs.Count != 8)
                return false;

            // 첫 데이터가 0이 아니면 잘못된 패킷
            if (messageSubs[0] != "0")
                return false;

            return true;
        }

        private void DeviceProcess()
        {
            string timestemp = DateTime.Now.ToString("mm:ss");

            // messageSubs[0] == start data
            float temp = Convert.ToSingle(messageSubs[6]);
            float humi = Convert.ToSingle(messageSubs[7]);

            chartTH.Series["Temperature"].Points.AddXY(timestemp, temp);
            chartTH.Series["Humidity"].Points.AddXY(timestemp, humi);
            if (chartTH.Series[0].Points.Count >= 50)
            {
                chartTH.Series["Temperature"].Points.RemoveAt(0);
                chartTH.Series["Humidity"].Points.RemoveAt(0);
            }

            string strSql = "";
            for (int i = 0; i < 5; ++i)
            {
                bool enabled = messageSubs[i + 1] == "1";
                bool changed = (enabled && controlLabels[i].Text == "OFF") || (enabled == false && controlLabels[i].Text == "ON") ? true : false;
                controlLabels[i].Text = enabled ? "ON" : "OFF";
                controlLabels[i].ForeColor = enabled ? Color.DarkGreen : Color.Red;
                controlImages[i].Image = imageList.Images[Convert.ToInt32(enabled)];


                // sql insert
                if (changed)
                {
                    if (enabled)
                    {
                        strSql = $"insert into deviceworkingtb values('Line1'" +
                            $",'Device{i + 1}','{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}','','{enabled}')";
                    }
                    else
                    {
                        strSql = $"update deviceworkingtb set EndTime='{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}' where DeviceName='Device{i + 1}'";
                    }
                    Sql(strSql);
                }
            }
            strSql = "select * from deviceworkingtb";
            SqlSelect(strSql, dataGV_work);
        }

        private bool ReceiveDataPacket(byte data)
        {
            switch (rxLen)
            {
                case 0:
                    if (data == 0x5b) // 91 [
                    {
                        rxBuf[rxLen++] = data;
                    }
                    break;

                default:
                    rxBuf[rxLen++] = data;
                    if (data == 0x5d) // 93 ]
                        return true;
                    break;
            }
            return false;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            tbProductNo.Focus();
            tbProductNo.Select(0, tbProductNo.Text.Length);

            serialDevice.DataBits = 8;
            serialDevice.StopBits = StopBits.One;
            serialDevice.Parity = Parity.None;
            cbPort.DataSource = SerialPort.GetPortNames();

            controlLabels.Add(lbDev1Stats);
            controlLabels.Add(lbDev2Stats);
            controlLabels.Add(lbDev3Stats);
            controlLabels.Add(lbSens1Stats);
            controlLabels.Add(lbSens2Stats);

            controlImages.Add(pbDev1);
            controlImages.Add(pbDev2);
            controlImages.Add(pbDev3);
            controlImages.Add(pbSensor1);
            controlImages.Add(pbSensor2);
        }

        private void DBConnection(string ip, string db_name, string userid, string pw)
        {
            string connectionString;
            connectionString = $"server={ip}; database={db_name}; uid={userid}; password={pw};";
            connection = new MySqlConnection(connectionString);

        }

        private void AddProduct()
        {
            string pd_id = tbProductNo.Text;
            string pd_name = tbProductName.Text;
            string pd_ea = tbProductEa.Text;
            string pd_cost = tbProductCost.Text;
            string pd_company = tbProductCompany.Text;
            string pd_etc = tbEtc.Text;

            // sql insert
            string strSql = $"insert into inventorytb values('{pd_id}'" +
                $",'{pd_name}','{pd_ea}','{pd_cost}','{pd_company}','{pd_etc}')";

            Sql(strSql);
            strSql = "select * from inventorytb";
            SqlSelect(strSql, dataGV);

        }


        private void tbProductNo_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                tbProductName.Focus();
                tbProductName.Select(0, tbProductName.Text.Length);
            }
        }

        private void tbProductName_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                tbProductEa.Focus();
                tbProductEa.Select(0, tbProductEa.Text.Length);
            }
        }

        private void tbProductEa_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                tbProductCost.Focus();
                tbProductCost.Select(0, tbProductCost.Text.Length);
            }
        }

        private void tbProductCost_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                tbProductCompany.Focus();
                tbProductCompany.Select(0, tbProductCompany.Text.Length);
            }
        }

        private void tbProductCompany_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                tbEtc.Focus();
                tbEtc.Select(0, tbEtc.Text.Length);
            }
        }

        private void tbEtc_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                tbProductNo.Focus();
                tbProductNo.Select(0, tbProductNo.Text.Length);

                AddProduct();
            }
        }

        private void btnCreate_Click(object sender, EventArgs e)
        {
            AddProduct();
        }

        private void btnProductAll_Click(object sender, EventArgs e)
        {
            SqlSelect("select * from inventorytb", dataGV);
        }

        private void btnProductNo_Click(object sender, EventArgs e)
        {
            SqlSelect($"select * from inventorytb where ProductNo like '%{tbSearch.Text}%'", dataGV);
        }

        private void btnProductName_Click(object sender, EventArgs e)
        {
            SqlSelect($"select * from inventorytb where ProductName like '%{tbSearch.Text}%'", dataGV);
        }

        private void btnProductCompany_Click(object sender, EventArgs e)
        {
            SqlSelect($"select * from inventorytb where Company like '%{tbSearch.Text}%'", dataGV);
        }

        private void btnProductNoSearch_in_Click(object sender, EventArgs e)
        {
            SqlSelect($"select * from inventorytb where ProductNo like '%{tbProductNoSearch_in.Text}%'", dataGV_in_list);
        }

        private void btnProductNoSearch_out_Click(object sender, EventArgs e)
        {
            SqlSelect($"select * from inventorytb where ProductNo like '%{tbProductNoSearch_out.Text}%'", dataGV_out_list);
        }

        private void dataGV_in_list_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            tbProductNo_in.Text = dataGV_in_list.Rows[e.RowIndex].Cells[0].Value.ToString();
            tempProductName = dataGV_in_list.Rows[e.RowIndex].Cells[1].Value.ToString();
        }

        private void tbProductNoSearch_in_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
                btnProductNoSearch_in_Click(sender, e);
        }

        private void dataGV_out_list_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            tbProductNo_out.Text = dataGV_out_list.Rows[e.RowIndex].Cells[0].Value.ToString();
            tempProductName = dataGV_out_list.Rows[e.RowIndex].Cells[1].Value.ToString();
        }

        private void tbProductNoSearch_out_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
                btnProductNoSearch_out_Click(sender, e);
        }

        private void btnInput_Click(object sender, EventArgs e)
        {
            if (tbProductNo_in.Text == "")
            {
                MessageBox.Show("제품이 선택되지 않았습니다.");
                return;
            }

            int inEa = Convert.ToInt32(tbInputEa.Text);
            if (tbInputEa.Text == "" || inEa == 0)
            {
                MessageBox.Show("입고 수량을 확인해주세요.");
            }

            string data = SqlGet($"select Ea from inventorytb where ProductNo like '%{tbProductNo_in.Text}%'");
            int ea = Convert.ToInt32(data) + inEa;

            string sql = $"insert into inputtb(ProductNo, ProductName, Ea, InputDate) " +
                $"values ('{tbProductNo_in.Text}','{tempProductName}'," +
                $"{tbInputEa.Text},'{DateTime.Now.ToString("yyyy-MM-dd")}')";
            Sql(sql);
            SqlSelect("select * from inputtb", dataGV_in_rt);

            sql = $"update inventorytb set Ea={ea} where ProductNo='{tbProductNo_in.Text}'";
            Sql(sql);
            SqlSelect("select * from inventorytb", dataGV_in_list);
            SqlSelect("select * from inventorytb", dataGV);
            SqlSelect("select * from inventorytb", dataGV_out_list);
        }

        private void btnOutput_Click(object sender, EventArgs e)
        {
            if (tbProductNo_out.Text == "")
            {
                MessageBox.Show("제품이 선택되지 않았습니다.");
                return;
            }

            int outEa = Convert.ToInt32(tbOutputEa.Text);
            if (tbOutputEa.Text == "" || outEa == 0)
            {
                MessageBox.Show("출고 수량을 확인해주세요.");
                return;
            }

            string data = SqlGet($"select Ea from inventorytb where ProductNo like '%{tbProductNo_out.Text}%'");
            int ea = Convert.ToInt32(data) - outEa;

            if (ea < 0)
            {
                MessageBox.Show("출고 수량이 너무 많아요.");
                return;
            }

            string sql = $"insert into outputtb(ProductNo, ProductName, Ea, OutputDate) " +
                $"values ('{tbProductNo_out.Text}','{tempProductName}'," +
                $"{tbOutputEa.Text},'{DateTime.Now.ToString("yyyy-MM-dd")}')";
            Sql(sql);
            SqlSelect("select * from outputtb", dataGV_out_rt);

            sql = $"update inventorytb set Ea={ea} where ProductNo='{tbProductNo_out.Text}'";
            Sql(sql);
            SqlSelect("select * from inventorytb", dataGV_out_list);
            SqlSelect("select * from inventorytb", dataGV);
            SqlSelect("select * from inventorytb", dataGV_in_list);
        }

        private void tabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
            switch(tabControl1.SelectedTab.Text)
            {
                case "가동시간":

                    SqlSelect("select * from deviceworkingtb", dataGV_work);
                    break;
            }
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            if (serialDevice.IsOpen)
            {
                try
                {
                    serialDevice.Close();
                    btnStart.Text = "실행하기";
                } 
                catch (Exception err)
                {
                    MessageBox.Show(err.Message);
                }
            }
            else
            {
                serialDevice.PortName = cbPort.Text;
                serialDevice.BaudRate = Convert.ToInt32(cbBaud.Text);

                try
                {
                    serialDevice.Open();
                    btnStart.Text = "종료하기";
                }
                catch
                {
                    MessageBox.Show("serial open error");
                }
            }
        }

        private void lbDevice1_Click(object sender, EventArgs e)
        {
            if (serialDevice.IsOpen == false)
                return;

            serialDevice.Write("[1,0]");
        }

        private void lbDevice2_Click(object sender, EventArgs e)
        {
            if (serialDevice.IsOpen == false)
                return;

            serialDevice.Write("[1,1]");
        }

        private void lbDevice3_Click(object sender, EventArgs e)
        {
            if (serialDevice.IsOpen == false)
                return;

            serialDevice.Write("[1,2]");
        }
    }
}
