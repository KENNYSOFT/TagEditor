using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Data.SQLite;
using System.Reflection;
using Microsoft.VisualBasic;
using Microsoft.WindowsAPICodePack.Taskbar;

namespace TagEditor
{
    public partial class TagEditor : Form
    {
        public TagEditor()
        {
            InitializeComponent();
            checkBox1.Checked = true;
            setOpened(false);
            taskbarManager = TaskbarManager.Instance;
        }

        private string filePath, newFilePath;
        private SQLiteConnection conn;
        private int now = -1, next = -1, prev = -1;
        private bool opened, changed;
        private TaskbarManager taskbarManager;

        public static string getDBFilePath(string filePath)
        {
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Title = "파일 변환";
            sfd.FileName = getFileName(filePath).Replace(getFileName(filePath).Split(new string[] { "." }, StringSplitOptions.None).Last(), "db");
            sfd.Filter = "SQLite DB 파일 (*.db)|*.db";
            DialogResult dr = sfd.ShowDialog();
            if (dr == DialogResult.OK) return sfd.FileName;
            else return null;
        }

        public static string getTextFilePath(string filePath)
        {
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Title = "파일 저장";
            sfd.FileName = getFileName(filePath).Replace(".db", ".txt");
            sfd.Filter = "텍스트 파일 (*.txt)|*.txt|태그 파일 (*.tag)|*.tag|모든 파일 (*.*)|*.*";
            DialogResult dr = sfd.ShowDialog();
            if (dr == DialogResult.OK) return sfd.FileName;
            else return null;
        }

        public static string getFileName(string filePath)
        {
            return filePath.Split(new string[] { "\\" }, StringSplitOptions.None).Last();
        }

        public static string getFileDirectory(string filePath)
        {
            return filePath.Replace(getFileName(filePath), "");
        }

        public static List<string> getResultList(string change)
        {
            if (change.Length == 0) return null;
            List<string> resultList = new List<string>();
            foreach (string s in change.Split(new string[] { "+/SW" }, StringSplitOptions.None))
            {
                resultList.AddRange(s.Split(new string[] { "+" }, StringSplitOptions.RemoveEmptyEntries));
                resultList.Add("+/SW");
            }
            resultList.RemoveAt(resultList.Count - 1);
            foreach (string s in resultList)
            {
                if (s == "//SP") continue;
                if (s.Count(c => c == '/') != 1 || s.Contains(' ') || s.Contains('\t')) return null;
            }
            return resultList;
        }

        public static string getWord(ListBox.ObjectCollection items)
        {
            string word = "";
            foreach (string s in items)
            {
                if (s == "//SP") word = word + "/";
                else word = word + s.Split(new string[] { "/", "__" }, StringSplitOptions.None)[0];
            }
            return word;
        }

        public static string getMidTag(string leftTag, string rightTag)
        {
            try
            {
                int leftNum = Convert.ToInt32(leftTag.Substring(leftTag.LastIndexOf("-") + 1)), rightNum = Convert.ToInt32(rightTag.Substring(rightTag.LastIndexOf("-") + 1));
                return rightTag.Replace(rightNum.ToString(), ((leftNum + rightNum) / 2).ToString());
            }
            catch
            {
                return leftTag;
            }
        }

        public void makeTitle(bool opened, bool changed)
        {
            Text = GetType().Name + " v" + Assembly.GetExecutingAssembly().GetName().Version.ToString();
            if (opened)
            {
                if (changed) Text = getFileName(filePath) + "* [" + getFileDirectory(filePath) + "] - " + Text;
                else Text = getFileName(filePath) + " [" + getFileDirectory(filePath) + "] - " + Text;
            }
        }

        public void makeTitle()
        {
            makeTitle(opened, changed);
        }

        public void executeSQLiteCommand(string command)
        {
            using (SQLiteCommand cmd = new SQLiteCommand(command, conn))
            {
                cmd.ExecuteNonQuery();
            }
        }

        public bool commitChange()
        {
            if ((now != -1 ? listBox1.Items.Count > 0 : true) && (next != -1 ? listBox2.Items.Count > 0 : true) && (prev != -1 ? listBox3.Items.Count > 0 : true))
            {
                label4.Text = getWord(listBox1.Items);
                executeSQLiteCommand("UPDATE tageditor SET word = '" + getWord(listBox1.Items).Replace("'", "''") + "', result = '" + String.Join("+", listBox1.Items.Cast<string>()).Replace("'", "''") + "' WHERE _id = '" + now + "';");

                label5.Text = getWord(listBox2.Items);
                if (next != -1)
                {
                    if (containsRow(next)) executeSQLiteCommand("UPDATE tageditor SET word = '" + getWord(listBox2.Items).Replace("'", "''") + "', result = '" + String.Join("+", listBox2.Items.Cast<string>()).Replace("'", "''") + "' WHERE _id = '" + next + "';");
                    else executeSQLiteCommand("INSERT INTO tageditor(_id, istagged, tag, word, result, content) VALUES(" + next + ", 1, '" + label2.Text.Replace("'", "''") + "', '" + getWord(listBox2.Items).Replace("'", "''") + "', '" + String.Join("+", listBox2.Items.Cast<string>()).Replace("'", "''") + "', NULL);");
                }

                label6.Text = getWord(listBox3.Items);
                if (prev != -1)
                {
                    if (containsRow(prev)) executeSQLiteCommand("UPDATE tageditor SET word = '" + getWord(listBox3.Items).Replace("'", "''") + "', result = '" + String.Join("+", listBox3.Items.Cast<string>()).Replace("'", "''") + "' WHERE _id = '" + prev + "';");
                    else executeSQLiteCommand("INSERT INTO tageditor(_id, istagged, tag, word, result, content) VALUES(" + prev + ", 1, '" + label3.Text.Replace("'", "''") + "', '" + getWord(listBox3.Items).Replace("'", "''") + "', '" + String.Join("+", listBox3.Items.Cast<string>()).Replace("'", "''") + "', NULL);");
                }

                MessageBox.Show("적용이 완료되었습니다. 반드시 단어를 확인해 주시기 바랍니다.", "적용", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                setChanged(false);
                return true;
            }
            else
            {
                MessageBox.Show("빈 어절이 있습니다.", "적용", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        public int getRowNumber(string where)
        {
            if (conn == null) return -1;
            using (SQLiteCommand cmd = new SQLiteCommand("SELECT _id FROM tageditor WHERE " + where + ";", conn))
            {
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    int now = -1;
                    if (reader.Read()) now = Convert.ToInt32((Int64)reader["_id"]);
                    return now;
                }
            }
        }

        public int getNextRowNumber(int now)
        {
            return getRowNumber("_id > " + now + " ORDER BY _id ASC");
        }

        public int getPreviousRowNumber(int now)
        {
            return getRowNumber("_id < " + now + " ORDER BY _id DESC");
        }

        public int getNextTaggedRowNumber(int now)
        {
            return getRowNumber("_id > " + now + " AND istagged = 1 ORDER BY _id ASC");
        }

        public int getPreviousTaggedRowNumber(int now)
        {
            return getRowNumber("_id < " + now + " AND istagged = 1 ORDER BY _id DESC");
        }
        
        public bool containsRow(int start, int end)
        {
            using (SQLiteCommand cmd = new SQLiteCommand("SELECT COUNT(*) FROM tageditor WHERE _id > " + start + " AND _id < " + end + ";", conn))
            {
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read()) return Convert.ToInt32((Int64)reader[0]) > 0;
                    return false;
                }
            }
        }

        public bool containsRow(int row)
        {
            using (SQLiteCommand cmd = new SQLiteCommand("SELECT COUNT(*) FROM tageditor WHERE _id = " + row + ";", conn))
            {
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read()) return Convert.ToInt32((Int64)reader[0]) > 0;
                    return false;
                }
            }
        }

        public void drawNowTag()
        {
            if (conn == null)
            {
                label1.Text = "";
                label4.Text = "";
                listBox1.Items.Clear();
                return;
            }
            using (SQLiteCommand cmd = new SQLiteCommand("SELECT tag, word, result FROM tageditor WHERE _id = " + now + ";", conn))
            {
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        label1.Text = (string)reader["tag"];
                        label4.Text = (string)reader["word"];
                        listBox1.Items.Clear();
                        foreach (string s in ((string)reader["result"]).Split(new string[] { "+/SW" }, StringSplitOptions.None))
                        {
                            listBox1.Items.AddRange(s.Split(new string[] { "+" }, StringSplitOptions.RemoveEmptyEntries));
                            listBox1.Items.Add("+/SW");
                        }
                        listBox1.Items.RemoveAt(listBox1.Items.Count - 1);
                    }
                    else
                    {
                        label1.Text = "";
                        label4.Text = "";
                        listBox1.Items.Clear();
                    }
                }
            }
        }

        public void drawNextTag()
        {
            if (conn == null)
            {
                label2.Text = "";
                label5.Text = "";
                listBox2.Items.Clear();
                return;
            }
            using (SQLiteCommand cmd = new SQLiteCommand("SELECT tag, word, result FROM tageditor WHERE _id = " + next + ";", conn))
            {
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        label2.Text = (string)reader["tag"];
                        label5.Text = (string)reader["word"];
                        listBox2.Items.Clear();
                        foreach (string s in ((string)reader["result"]).Split(new string[] { "+/SW" }, StringSplitOptions.None))
                        {
                            listBox2.Items.AddRange(s.Split(new string[] { "+" }, StringSplitOptions.RemoveEmptyEntries));
                            listBox2.Items.Add("+/SW");
                        }
                        listBox2.Items.RemoveAt(listBox2.Items.Count - 1);
                    }
                    else
                    {
                        label2.Text = "";
                        label5.Text = "";
                        listBox2.Items.Clear();
                    }
                }
            }
        }

        public void drawPreviousTag()
        {
            if (conn == null)
            {
                label3.Text = "";
                label6.Text = "";
                listBox3.Items.Clear();
                return;
            }
            using (SQLiteCommand cmd = new SQLiteCommand("SELECT tag, word, result FROM tageditor WHERE _id = " + prev + ";", conn))
            {
                using (SQLiteDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        label3.Text = (string)reader["tag"];
                        label6.Text = (string)reader["word"];
                        listBox3.Items.Clear();
                        foreach (string s in ((string)reader["result"]).Split(new string[] { "+/SW" }, StringSplitOptions.None))
                        {
                            listBox3.Items.AddRange(s.Split(new string[] { "+" }, StringSplitOptions.RemoveEmptyEntries));
                            listBox3.Items.Add("+/SW");
                        }
                        listBox3.Items.RemoveAt(listBox3.Items.Count - 1);
                    }
                    else
                    {
                        label3.Text = "";
                        label6.Text = "";
                        listBox3.Items.Clear();
                    }
                }
            }
        }

        public void drawTag()
        {
            next = getNextTaggedRowNumber(now);
            prev = getPreviousTaggedRowNumber(now);
            drawNowTag();
            drawNextTag();
            drawPreviousTag();
        }

        public void setOpened(bool opened)
        {
            if (opened)
            {
                button2.Enabled = true;
                button3.Enabled = true;
                button4.Enabled = true;
                button5.Enabled = true;
                button6.Enabled = true;
                button7.Enabled = true;
                textBox1.Enabled = true;
                label4.Enabled = true;
                label5.Enabled = true;
                label6.Enabled = true;
                button8.Enabled = true;
                button9.Enabled = true;
                button10.Enabled = true;
                button11.Enabled = true;
                button12.Enabled = true;
                button13.Enabled = true;
                checkBox1.Enabled = true;
                checkBox2.Enabled = true;
                checkBox3.Enabled = true;
                checkBox4.Enabled = true;
            }
            else
            {
                button2.Enabled = false;
                button3.Enabled = false;
                button4.Enabled = false;
                button5.Enabled = false;
                button6.Enabled = false;
                button7.Enabled = false;
                textBox1.Enabled = false;
                label4.Enabled = false;
                label5.Enabled = false;
                label6.Enabled = false;
                button8.Enabled = false;
                button9.Enabled = false;
                button10.Enabled = false;
                button11.Enabled = false;
                button12.Enabled = false;
                button13.Enabled = false;
                checkBox1.Enabled = false;
                checkBox2.Enabled = false;
                checkBox3.Enabled = false;
                checkBox4.Enabled = false;
            }
            this.opened = opened;
            makeTitle();
        }

        public void setChanged(bool changed)
        {
            if (changed)
            {
                button2.Enabled = false;
                button4.Enabled = false;
                button5.Enabled = false;
                button6.Enabled = false;
                button7.Enabled = false;
                button8.Enabled = (now != -1 ? listBox1.Items.Count > 0 : true) && (next != -1 ? listBox2.Items.Count > 0 : true) && (prev != -1 ? listBox3.Items.Count > 0 : true);
                button9.Enabled = true;
            }
            else
            {
                button2.Enabled = true;
                button4.Enabled = true;
                button5.Enabled = true;
                button6.Enabled = true;
                button7.Enabled = true;
                button8.Enabled = false;
                button9.Enabled = false;
            }
            this.changed = changed;
            makeTitle();
        }

        private void TagEditor_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (changed)
            {
                switch (MessageBox.Show("적용하지 않은 변경사항이 있습니다. 적용하시겠습니까?", "종료", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning))
                {
                case DialogResult.Yes:
                    if (!commitChange()) e.Cancel = true;
                    break;
                case DialogResult.No:
                    break;
                case DialogResult.Cancel:
                    e.Cancel = true;
                    break;
                }
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (changed)
            {
                switch (MessageBox.Show("적용하지 않은 변경사항이 있습니다. 적용하시겠습니까?", "파일 열기", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning))
                {
                case DialogResult.Yes:
                    if (!commitChange()) return;
                    break;
                case DialogResult.No:
                    break;
                case DialogResult.Cancel:
                    return;
                }
            }
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Title = "파일 열기";
            ofd.FileName = "";
            ofd.Filter = "SQLite DB 파일 (*.db)|*.db|텍스트 파일 (*.txt)|*.txt|태그 파일 (*.tag)|*.tag|모든 파일 (*.*)|*.*";
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                setOpened(false);
                if (conn != null)
                {
                    conn.Close();
                    GC.Collect();
                    conn = null;
                }
                drawTag();

                filePath = ofd.FileName;
                makeTitle(true, false);

                if (!filePath.EndsWith(".db"))
                {
                    if (MessageBox.Show("SQLite DB 파일로 변환하시겠습니까?", "파일 변환", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK && (newFilePath = getDBFilePath(filePath)) != null)
                    {
                        Encoding encoding;
                        if (MessageBox.Show("UTF-8 파일이면 '예', 아니면 '아니오'를 눌러주세요.", "파일 변환", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) encoding = Encoding.UTF8;
                        else encoding = Encoding.Default;

                        int columns;
                        if (MessageBox.Show("태그가 있는 파일이면 '예', 아니면 '아니오'를 눌러주세요.", "파일 변환", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) columns = 3;
                        else columns = 2;

                        File.Delete(newFilePath);

                        conn = new SQLiteConnection(@"Data Source=" + newFilePath);
                        conn.Open();

                        executeSQLiteCommand("CREATE TABLE IF NOT EXISTS tageditor(_id INTEGER PRIMARY KEY, istagged BOOLEAN, tag VARCHAR(-1), word VARCHAR(-1), result VARCHAR(-1), content VARCHAR(-1));");

                        taskbarManager.SetProgressState(TaskbarProgressBarState.Indeterminate);
                        int lines = File.ReadLines(filePath).Count(), now = 0;
                        progressBar1.Minimum = 0;
                        progressBar1.Maximum = lines;

                        using (StreamReader sr = new StreamReader(filePath, encoding))
                        {
                            taskbarManager.SetProgressState(TaskbarProgressBarState.Normal);
                            string line;
                            List<string> rowList = new List<string>();
                            while ((line = sr.ReadLine()) != null)
                            {
                                now++;
                                line = line.Replace("'", "''");
                                string[] col = line.Split(new string[] { "\t" }, StringSplitOptions.None);
                                if (!line.StartsWith("<======") && col.Length == columns && col.Count(s => s.Length != 0) == columns)
                                {
                                    if (columns == 3) rowList.Add("(" + (now * 100) + ", 1, '" + String.Join("', '", col) + "', NULL)");
                                    if (columns == 2) rowList.Add("(" + (now * 100) + ", 1, '', '" + String.Join("', '", col) + "', NULL)");
                                }
                                else rowList.Add("(" + (now * 100) + ", 0, NULL, NULL, NULL, '" + line + "')");
                                if (rowList.Count == 100000)
                                {
                                    executeSQLiteCommand("INSERT INTO tageditor(_id, istagged, tag, word, result, content) VALUES" + String.Join(", ", rowList) + ";");
                                    rowList.Clear();
                                }
                                progressBar1.Value = now;
                                taskbarManager.SetProgressValue(now, lines);
                            }
                            if (rowList.Count > 0)
                            {
                                executeSQLiteCommand("INSERT INTO tageditor(_id, istagged, tag, word, result, content) VALUES" + String.Join(", ", rowList) + ";");
                                rowList.Clear();
                            }
                            taskbarManager.SetProgressState(TaskbarProgressBarState.NoProgress);
                        }

                        filePath = newFilePath;
                    }
                    makeTitle();
                }

                if (filePath.EndsWith(".db"))
                {
                    setOpened(true);

                    progressBar1.Minimum = 0;
                    progressBar1.Maximum = 1;
                    progressBar1.Value = 1;

                    conn = new SQLiteConnection(@"Data Source=" + filePath);
                    conn.Open();

                    now = getRowNumber("istagged = 1");
                    drawTag();
                    setChanged(false);
                }
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (conn == null)
            {
                MessageBox.Show("DB 파일을 열어주세요.", "파일 저장", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (filePath.EndsWith(".db") && (newFilePath = getTextFilePath(filePath)) != null)
            {
                int lines = 0, now = 0;
                using (SQLiteCommand cmd = new SQLiteCommand("SELECT COUNT(*) FROM tageditor;", conn))
                {
                    using (SQLiteDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            lines = Convert.ToInt32((Int64)reader[0]);
                            progressBar1.Minimum = 1;
                            progressBar1.Maximum = lines;
                        }
                    }
                }

                using (StreamWriter sw = new StreamWriter(newFilePath))
                {
                    taskbarManager.SetProgressState(TaskbarProgressBarState.Normal);
                    using (SQLiteCommand cmd = new SQLiteCommand("SELECT istagged, tag, word, result, content FROM tageditor ORDER BY _id ASC;", conn))
                    {
                        using (SQLiteDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                now++;
                                bool istagged = (bool)reader["istagged"];
                                if (!istagged) sw.WriteLine((string)reader["content"]);
                                else sw.WriteLine((string)reader["tag"] + "\t" + (string)reader["word"] + "\t" + (string)reader["result"]);
                                progressBar1.Value = now;
                                taskbarManager.SetProgressValue(now, lines);
                            }
                        }
                    }
                    taskbarManager.SetProgressState(TaskbarProgressBarState.NoProgress);
                }
            }
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            if (!checkBox1.Checked && !checkBox2.Checked && !checkBox3.Checked) checkBox1.Checked = true;
        }

        private void checkBox2_CheckedChanged(object sender, EventArgs e)
        {
            if (!checkBox1.Checked && !checkBox2.Checked && !checkBox3.Checked) checkBox2.Checked = true;
        }

        private void checkBox3_CheckedChanged(object sender, EventArgs e)
        {
            if (!checkBox1.Checked && !checkBox2.Checked && !checkBox3.Checked) checkBox3.Checked = true;
        }

        private void button3_Click(object sender, EventArgs e)
        {
            if (conn == null)
            {
                MessageBox.Show("DB 파일을 열어주세요.", "검색", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            List<string> where = new List<string>();
            if (checkBox1.Checked)
            {
                if (!checkBox4.Checked) where.Add("UPPER(tag) LIKE '%" + textBox1.Text.Replace("'", "''").ToUpper() + "%'");
                else where.Add("tag = '" + textBox1.Text.Replace("'", "''") + "'");
            }
            if (checkBox2.Checked)
            {
                if (!checkBox4.Checked) where.Add("UPPER(word) LIKE '%" + textBox1.Text.Replace("'", "''").ToUpper() + "%'");
                else where.Add("word = '" + textBox1.Text.Replace("'", "''") + "'");
            }
            if (checkBox3.Checked)
            {
                if (!checkBox4.Checked) where.Add("UPPER(result) LIKE '%" + textBox1.Text.Replace("'", "''").ToUpper() + "%'");
                else where.Add("result = '" + textBox1.Text.Replace("'", "''") + "' OR result LIKE '" + textBox1.Text.Replace("'", "''") + "+%' OR result LIKE '%+" + textBox1.Text.Replace("'", "''") + "+%' OR result LIKE '%+" + textBox1.Text.Replace("'", "''") + "'");
            }
            int find = getRowNumber("_id > " + now + " AND (" + String.Join(" OR ", where) + ") ORDER BY _id ASC");
            if (find == -1) find = getRowNumber("_id <= " + now + " AND (" + String.Join(" OR ", where) + ") ORDER BY _id ASC");
            if (find != -1)
            {
                now = find;
                drawTag();
                setChanged(false);
            }
            else MessageBox.Show("검색 결과가 없습니다.", "검색", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void textBox1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) button3_Click(this, e);
        }

        private void label1_MouseHover(object sender, EventArgs e)
        {
            toolTip1.SetToolTip(label1, label1.Text);
        }

        private void label2_MouseHover(object sender, EventArgs e)
        {
            toolTip1.SetToolTip(label2, label2.Text);
        }

        private void label3_MouseHover(object sender, EventArgs e)
        {
            toolTip1.SetToolTip(label3, label3.Text);
        }

        private void label4_Click(object sender, EventArgs e)
        {
            if (now != -1 && !changed)
            {
                string change = Interaction.InputBox("변경할 단어 정보를 입력하세요.", "단어 변경", label4.Text);
                if (change.Length > 0)
                {
                    label4.Text = change;
                    executeSQLiteCommand("UPDATE tageditor SET word = '" + label4.Text.Replace("'", "''") + "' WHERE tag = '" + label1.Text + "';");
                }
            }
        }

        private void label5_Click(object sender, EventArgs e)
        {
            if (next != -1 && !changed)
            {
                string change = Interaction.InputBox("변경할 단어 정보를 입력하세요.", "단어 변경", label5.Text);
                if (change.Length > 0)
                {
                    label5.Text = change;
                    executeSQLiteCommand("UPDATE tageditor SET word = '" + label5.Text.Replace("'", "''") + "' WHERE tag = '" + label2.Text + "';");
                }
            }
        }

        private void label6_Click(object sender, EventArgs e)
        {
            if (prev != -1 && !changed)
            {
                string change = Interaction.InputBox("변경할 단어 정보를 입력하세요.", "단어 변경", label6.Text);
                if (change.Length > 0)
                {
                    label6.Text = change;
                    executeSQLiteCommand("UPDATE tageditor SET word = '" + label6.Text.Replace("'", "''") + "' WHERE tag = '" + label3.Text + "';");
                }
            }
        }

        private void button4_Click(object sender, EventArgs e)
        {
            if (changed)
            {
                switch (MessageBox.Show("적용하지 않은 변경사항이 있습니다. 적용하시겠습니까?", "삽입", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning))
                {
                case DialogResult.Yes:
                    if (!commitChange()) return;
                    break;
                case DialogResult.No:
                    drawTag();
                    break;
                case DialogResult.Cancel:
                    return;
                }
            }
            if (getNextRowNumber(now) == now + 1)
            {
                if (MessageBox.Show("DB 구조 업데이트가 필요합니다. 진행하시겠습니까?", "삽입", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                {
                    executeSQLiteCommand("CREATE TEMPORARY TABLE tmp(_newid INTEGER NOT NULL PRIMARY KEY, _id INTEGER UNIQUE);\n"
                        + "UPDATE tageditor SET _id = -_id;\n"
                        + "INSERT INTO tmp(_id) SELECT _id FROM tageditor ORDER BY _id DESC;\n"
                        + "UPDATE tageditor SET _id = (SELECT _newid * 100 FROM tmp WHERE tmp._id = tageditor._id);\n"
                        + "DROP TABLE tmp;\n"
                        + "VACUUM;");

                    now = getRowNumber("tag = '" + label1.Text + "';");
                    drawTag();
                }
                else return;
            }
            next = (now + getNextRowNumber(now)) / 2;
            label2.Text = getMidTag(label1.Text, label2.Text);
            label5.Text = "";
            listBox2.Items.Clear();
            setChanged(true);
        }

        private void button5_Click(object sender, EventArgs e)
        {
            if (changed)
            {
                switch (MessageBox.Show("적용하지 않은 변경사항이 있습니다. 적용하시겠습니까?", "삽입", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning))
                {
                case DialogResult.Yes:
                    if (!commitChange()) return;
                    break;
                case DialogResult.No:
                    drawTag();
                    break;
                case DialogResult.Cancel:
                    return;
                }
            }
            if (getPreviousRowNumber(now) == now - 1)
            {
                if (MessageBox.Show("DB 구조 업데이트가 필요합니다. 진행하시겠습니까?", "삽입", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                {
                    executeSQLiteCommand("CREATE TEMPORARY TABLE tmp(_newid INTEGER NOT NULL PRIMARY KEY, _id INTEGER UNIQUE);\n"
                        + "UPDATE tageditor SET _id = -_id;\n"
                        + "INSERT INTO tmp(_id) SELECT _id FROM tageditor ORDER BY _id DESC;\n"
                        + "UPDATE tageditor SET _id = (SELECT _newid * 100 FROM tmp WHERE tmp._id = tageditor._id);\n"
                        + "DROP TABLE tmp;\n"
                        + "VACUUM;");

                    now = getRowNumber("tag = '" + label1.Text + "';");
                    drawTag();
                }
                else return;
            }
            prev = (now + getPreviousRowNumber(now)) / 2;
            label3.Text = getMidTag(label1.Text, label3.Text);
            label6.Text = "";
            listBox3.Items.Clear();
            setChanged(true);
        }

        private void button6_Click(object sender, EventArgs e)
        {
            if (changed)
            {
                switch (MessageBox.Show("적용하지 않은 변경사항이 있습니다. 적용하시겠습니까?", "합치기", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning))
                {
                case DialogResult.Yes:
                    if (!commitChange()) return;
                    break;
                case DialogResult.No:
                    drawTag();
                    break;
                case DialogResult.Cancel:
                    return;
                }
            }
            if (next != -1)
            {
                if (!containsRow(now, next))
                {
                    if (MessageBox.Show("정말로 합치시겠습니까?", "합치기", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                    {
                        label4.Text = label4.Text + label5.Text;
                        int count = listBox2.Items.Count;
                        for (int i = 1; i <= count; ++i)
                        {
                            listBox1.Items.Add(listBox2.Items[0]);
                            listBox2.Items.RemoveAt(0);
                        }

                        executeSQLiteCommand("UPDATE tageditor SET word = '" + label4.Text.Replace("'", "''") + "', result = '" + String.Join("+", listBox1.Items.Cast<string>()).Replace("'", "''") + "' WHERE _id = '" + now + "';");
                        executeSQLiteCommand("DELETE FROM tageditor WHERE _id > " + now + " AND _id <= " + next + ";");

                        next = getNextTaggedRowNumber(next);
                        drawNextTag();
                    }
                }
                else MessageBox.Show("합칠 수 없는 대상입니다.", "합치기", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else MessageBox.Show("합칠 대상이 없습니다.", "합치기", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void button7_Click(object sender, EventArgs e)
        {
            if (changed)
            {
                switch (MessageBox.Show("적용하지 않은 변경사항이 있습니다. 적용하시겠습니까?", "합치기", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning))
                {
                case DialogResult.Yes:
                    if (!commitChange()) return;
                    break;
                case DialogResult.No:
                    drawTag();
                    break;
                case DialogResult.Cancel:
                    return;
                }
            }
            if (prev != -1)
            {
                if (!containsRow(prev, next))
                {
                    label4.Text = label6.Text + label4.Text;
                    int count = listBox3.Items.Count;
                    for (int i = 1; i <= count; ++i)
                    {
                        listBox1.Items.Insert(0, listBox3.Items[listBox3.Items.Count - 1]);
                        listBox3.Items.RemoveAt(listBox3.Items.Count - 1);
                    }

                    executeSQLiteCommand("UPDATE tageditor SET word = '" + label4.Text.Replace("'", "''") + "', result = '" + String.Join("+", listBox1.Items.Cast<string>()).Replace("'", "''") + "' WHERE _id = '" + now + "';");
                    executeSQLiteCommand("DELETE FROM tageditor WHERE _id >= " + prev + " AND _id < " + now + ";");

                    prev = getPreviousTaggedRowNumber(prev);
                    drawPreviousTag();
                }
                else MessageBox.Show("합칠 수 없는 대상입니다.", "합치기", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else MessageBox.Show("합칠 대상이 없습니다.", "합치기", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void listBox1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            int index = listBox1.IndexFromPoint(e.Location);
            if (index != ListBox.NoMatches)
            {
                string change = Interaction.InputBox("변경할 형태소 정보를 입력하세요.", "형태소 변경", (string)listBox1.Items[index]);
                List<string> resultList = getResultList(change);
                if (resultList != null)
                {
                    listBox1.Items.RemoveAt(index);
                    int count = resultList.Count;
                    for (int i = 1; i <= count; ++i)
                    {
                        listBox1.Items.Insert(index, resultList[resultList.Count - 1]);
                        resultList.RemoveAt(resultList.Count - 1);
                    }
                    setChanged(true);
                }
                else if (change.Length > 0) MessageBox.Show("형태소 정보 형식이 올바르지 않습니다.", "형태소 변경", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void listBox1_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) return;
            listBox1.DoDragDrop(listBox1.SelectedIndices, DragDropEffects.Move);
        }

        private void listBox1_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
            case Keys.Enter:
                if (listBox1.SelectedIndices.Count >= 1)
                {
                    int firstIndex = listBox1.SelectedIndices[0], count = listBox1.SelectedIndices.Count;
                    string change = Interaction.InputBox("변경할 형태소 정보를 입력하세요.", "형태소 변경", String.Join("+", listBox1.Items.Cast<string>().ToList().Where((s, i) => listBox1.SelectedIndices.Contains(i))));
                    List<string> resultList = getResultList(change);
                    if (resultList != null)
                    {
                        for (int i = 1; i <= count; ++i) listBox1.Items.RemoveAt(firstIndex);
                        count = resultList.Count;
                        for (int i = 1; i <= count; ++i)
                        {
                            listBox1.Items.Insert(firstIndex, resultList[resultList.Count - 1]);
                            resultList.RemoveAt(resultList.Count - 1);
                        }
                        setChanged(true);
                    }
                    else if (change.Length > 0) MessageBox.Show("형태소 정보 형식이 올바르지 않습니다.", "형태소 변경", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                break;
            case Keys.Delete:
                if (MessageBox.Show("선택된 형태소들을 삭제하시겠습니까?", "형태소 변경", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK) for (int i = listBox1.SelectedIndices.Count - 1; i >= 0; --i) listBox1.Items.RemoveAt(listBox1.SelectedIndices[i]);
                break;
            }
        }

        private void listBox2_DragOver(object sender, DragEventArgs e)
        {
            ListBox.SelectedIndexCollection data = (ListBox.SelectedIndexCollection)e.Data.GetData(typeof(ListBox.SelectedIndexCollection));
            if (data.Contains(listBox1.Items.Count - 1)) e.Effect = DragDropEffects.Move;
            else e.Effect = DragDropEffects.None;
        }

        private void listBox2_DragDrop(object sender, DragEventArgs e)
        {
            ListBox.SelectedIndexCollection data = (ListBox.SelectedIndexCollection)e.Data.GetData(typeof(ListBox.SelectedIndexCollection));
            if (data.Contains(listBox1.Items.Count - 1))
            {
                int count = data.Count;
                for (int i = 1; i <= count; ++i)
                {
                    listBox2.Items.Insert(0, listBox1.Items[listBox1.Items.Count - 1]);
                    listBox1.Items.RemoveAt(listBox1.Items.Count - 1);
                }
                setChanged(true);
            }
        }

        private void listBox3_DragOver(object sender, DragEventArgs e)
        {
            ListBox.SelectedIndexCollection data = (ListBox.SelectedIndexCollection)e.Data.GetData(typeof(ListBox.SelectedIndexCollection));
            if (data.Contains(0)) e.Effect = DragDropEffects.Move;
            else e.Effect = DragDropEffects.None;
        }

        private void listBox3_DragDrop(object sender, DragEventArgs e)
        {
            ListBox.SelectedIndexCollection data = (ListBox.SelectedIndexCollection)e.Data.GetData(typeof(ListBox.SelectedIndexCollection));
            if (data.Contains(0))
            {
                int count = data.Count;
                for (int i = 1; i <= count; ++i)
                {
                    listBox3.Items.Add(listBox1.Items[0]);
                    listBox1.Items.RemoveAt(0);
                }
                setChanged(true);
            }
        }

        private void button8_Click(object sender, EventArgs e)
        {
            commitChange();
        }

        private void button9_Click(object sender, EventArgs e)
        {
            drawTag();
            setChanged(false);
        }

        private void button10_Click(object sender, EventArgs e)
        {
            if (changed)
            {
                switch (MessageBox.Show("적용하지 않은 변경사항이 있습니다. 적용하시겠습니까?", "이동", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning))
                {
                case DialogResult.Yes:
                    if (!commitChange()) return;
                    break;
                case DialogResult.No:
                    break;
                case DialogResult.Cancel:
                    return;
                }
            }
            int find = getNextTaggedRowNumber(now);
            if (find != -1)
            {
                now = find;
                drawTag();
                setChanged(false);
            }
            else MessageBox.Show("더 이상 뒤로 이동할 수 없습니다.", "이동", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void button11_Click(object sender, EventArgs e)
        {
            if (changed)
            {
                switch (MessageBox.Show("적용하지 않은 변경사항이 있습니다. 적용하시겠습니까?", "이동", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning))
                {
                case DialogResult.Yes:
                    if (!commitChange()) return;
                    break;
                case DialogResult.No:
                    break;
                case DialogResult.Cancel:
                    return;
                }
            }
            int find = getPreviousTaggedRowNumber(now);
            if (find != -1)
            {
                now = find;
                drawTag();
                setChanged(false);
            }
            else MessageBox.Show("더 이상 앞으로 이동할 수 없습니다.", "이동", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void button12_Click(object sender, EventArgs e)
        {
            int count = 1;
            if (listBox1.SelectedIndices.Contains(listBox1.Items.Count - 1)) count = listBox1.SelectedIndices.Count;
            for (int i = 1; i <= count; ++i)
            {
                listBox2.Items.Insert(0, listBox1.Items[listBox1.Items.Count - 1]);
                listBox1.Items.RemoveAt(listBox1.Items.Count - 1);
            }
            setChanged(true);
        }

        private void button13_Click(object sender, EventArgs e)
        {
            int count = 1;
            if (listBox1.SelectedIndices.Contains(0)) count = listBox1.SelectedIndices.Count;
            for (int i = 1; i <= count; ++i)
            {
                listBox3.Items.Add(listBox1.Items[0]);
                listBox1.Items.RemoveAt(0);
            }
            setChanged(true);
        }
    }
}
