using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Xml;
using System.Reflection;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using System.Data.SqlClient;
using System.Data;
using System.Text.RegularExpressions;

namespace NimbleSQL
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private DataTable dt;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("NimbleSQL.Resources.SQL.xshd"))
            {
                using (var reader = new XmlTextReader(stream))
                {
                    sqlEditor.SyntaxHighlighting =
                        HighlightingLoader.Load(reader,
                        HighlightingManager.Instance);
                }
            }

            sqlEditor.TextArea.TextEntering += sqlEditor_TextEntering;
            sqlEditor.TextArea.TextEntered += sqlEditor_TextEntered;
        }

        CompletionWindow completionWindow;

        void sqlEditor_TextEntered(object sender, TextCompositionEventArgs e)
        {
            if (e.Text == ".")
            {
                var wordBeforeDot = string.Empty;

                var caretPosition = sqlEditor.CaretOffset - 2;

                var lineOffset = sqlEditor.Document.GetOffset(sqlEditor.Document.GetLocation(caretPosition));

                string text = sqlEditor.Document.GetText(lineOffset, 1);

                // Get text backward of the mouse position, until the first space
                while (!string.IsNullOrWhiteSpace(text))
                {
                    wordBeforeDot = text + wordBeforeDot;

                    if (caretPosition == 0)
                        break;

                    lineOffset = sqlEditor.Document.GetOffset(sqlEditor.Document.GetLocation(--caretPosition));

                    text = sqlEditor.Document.GetText(lineOffset, 1);
                }

                var words = wordBeforeDot.Split('.');

                // Open code completion after the user has pressed dot:
                completionWindow = new CompletionWindow(sqlEditor.TextArea);
                IList<ICompletionData> data = completionWindow.CompletionList.CompletionData;

                foreach(var word in words)
                {
                    data.Add(new MyCompletionData(word));
                }

                completionWindow.Show();
                completionWindow.Closed += delegate {
                    completionWindow = null;
                };
            }
        }

        void sqlEditor_TextEntering(object sender, TextCompositionEventArgs e)
        {
            if (e.Text.Length > 0 && completionWindow != null)
            {
                if (!char.IsLetterOrDigit(e.Text[0]))
                {
                    // Whenever a non-letter is typed while the completion window is open,
                    // insert the currently selected element.
                    completionWindow.CompletionList.RequestInsertion(e);
                }
            }
            // Do not set e.Handled=true.
            // We still want to insert the character that was typed.
        }

        private void btnExecute_Click(object sender, RoutedEventArgs e)
        {
            string ConString = "Server=(local); Connection Timeout=30; Database=WideWorldImporters; Trusted_Connection=True;";
            string CmdString = string.Empty;
            using (SqlConnection con = new SqlConnection(ConString))
            {
                CmdString = sqlEditor.Text;
                SqlCommand cmd = new SqlCommand(CmdString, con);
                SqlDataAdapter sda = new SqlDataAdapter(cmd);
                dt = new DataTable("Main");
                sda.Fill(dt);
                dgMain.ItemsSource = dt.DefaultView;
            }

            foreach(DataColumn col in dt.Columns)
            {
                col.DataType.ToString();
            }
        }

        private void btnCalculate_Click(object sender, RoutedEventArgs e)
        {
            string fullText = templateEditor.Text;

            string matchString = @"(\$ONCE|\$EACH)";
            var matches = Regex.Matches(fullText, matchString);


            var templateSections = new List<TemplateSection>();

            if (matches.Count == 0)
            {
                templateSections.Add(new TemplateSection(TemplateType.Each, fullText));
            }
            else if(matches[0].Index != 0)
            {
                var tString = fullText.Substring(0, matches[0].Index);

                if(!string.IsNullOrWhiteSpace(tString))
                    templateSections.Add(new TemplateSection(TemplateType.Each, tString));
            }


            for (var i = 0; i < matches.Count; i++)
            {
                int start = matches[i].Index + 5;

                // get rid of trailing newline chars
                while (start != fullText.Length - 1 && (fullText[start] == '\r' || fullText[start] == '\n'))
                    start++;

                int length = fullText.Length - start;

                if(i < matches.Count - 1)
                    length = matches[i + 1].Index - start;

                switch(matches[i].Value)
                {
                    case "$EACH":
                        templateSections.Add(new TemplateSection(TemplateType.Each, fullText.Substring(start, length)));
                        break;
                    case "$ONCE":   
                        templateSections.Add(new TemplateSection(TemplateType.Once, fullText.Substring(start, length)));
                        break;
                }
            }

            System.Text.StringBuilder newResult = new System.Text.StringBuilder();

            string result = String.Empty;

            string columnMatcher = String.Empty;
            string columnNames = String.Empty;

            for(var x = 0; x < dt.Columns.Count; x++)
            {
                if (x != 0)
                {
                    columnNames += "|" + Regex.Escape("$[" + dt.Columns[x].ColumnName + "]");
                    columnMatcher += "|" + Regex.Escape("$" + x);
                }
                else
                {
                    columnNames += Regex.Escape("$[" + dt.Columns[x].ColumnName + "]");
                    columnMatcher += Regex.Escape("$" + x);
                }
                    
            }

            var regex = new Regex(columnMatcher);
            var regex2 = new Regex(columnNames);

            foreach (var section in templateSections)
            {
                switch(section.TemplateType)
                {
                    case TemplateType.Once:
                        newResult.Append(section.TemplateString);
                        break;
                    case TemplateType.Each:
                        for (var n = 0; n < dt.Rows.Count; n++)
                        {
                            
                            var test =  regex.Replace(section.TemplateString, m => dt.Rows[n][Int32.Parse(m.Value.Replace("$", String.Empty))].ToString());
                           
                            newResult.Append(regex2.Replace(test, m => dt.Rows[n][m.Value.Substring(2, m.Value.Length - 3)].ToString()));
                        }
                        break;
                }

            }

            outputEditor.Text = newResult.ToString();
        }
    }

    /// Implements AvalonEdit ICompletionData interface to provide the entries in the
    /// completion drop down.
    public class MyCompletionData : ICompletionData
    {
        public MyCompletionData(string text)
        {
            this.Text = text;
        }

        public System.Windows.Media.ImageSource Image
        {
            get { return null; }
        }

        public string Text { get; private set; }

        // Use this property if you want to show a fancy UIElement in the list.
        public object Content
        {
            get { return this.Text; }
        }

        public object Description
        {
            get { return "Description for " + this.Text; }
        }

        public void Complete(TextArea textArea, ISegment completionSegment,
            EventArgs insertionRequestEventArgs)
        {
            textArea.Document.Replace(completionSegment, this.Text);
        }

        public double Priority { get { return 0; } }
    }

    public enum TemplateType
    {
        Each,
        Once
    }

    public class TemplateSection
    {
        public TemplateSection(TemplateType templateType, string templateString)
        {
            TemplateType = templateType;
            TemplateString = templateString;
        }

        public TemplateType TemplateType { get; set; }
        public string TemplateString { get; set; }
    }
}
