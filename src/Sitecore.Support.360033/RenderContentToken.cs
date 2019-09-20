using System.Linq;
using System.Text.RegularExpressions;
using System.Web.UI;
using Microsoft.Extensions.DependencyInjection;
using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.DependencyInjection;
using Sitecore.Diagnostics;
using Sitecore.Pipelines.RenderField;
using Sitecore.Web;
using Sitecore.XA.Feature.ContentTokens;
using Sitecore.XA.Foundation.Abstractions;
using Sitecore.XA.Foundation.Multisite;
using Sitecore.XA.Foundation.Multisite.Extensions;
namespace Sitecore.Support.XA.Feature.ContentTokens.Pipelines.RenderField
{
    public class RenderContentToken
    {
        protected Regex TextTokenInputs { get; } = new Regex(@"\$\(([^$()]+)\)", RegexOptions.Compiled);
        protected Regex RichTextTokenSpans { get; } = new Regex("<span.*?data-variableid=\"([^ \"]*)\"[^>]*>.*?</span>", RegexOptions.Compiled);
        protected string[] SupportFields { get; } = { "rich text", "single-line text", "multi-line text" };
        protected IPageMode PageMode { get; }
        protected IContext Context { get; }

        public RenderContentToken(IPageMode pageMode, IContext context)
        {
            PageMode = pageMode;
            Context = context;
        }

        public void Process(RenderFieldArgs args)
        {
            Assert.ArgumentNotNull(args, "args");
            Assert.ArgumentNotNull(args.FieldTypeKey, "args.FieldTypeKey");

            if (!Context.Site.IsSxaSite() ||
                ((!SupportFields.Contains(args.FieldTypeKey) || string.IsNullOrEmpty(args.FieldValue) || PageMode.IsExperienceEditorEditing) && !PageMode.IsNormal))
            {
                return;
            }

            TransformWebControls(args);
        }

        protected virtual void TransformWebControls(RenderFieldArgs args)
        {
            Page page = new Page { AppRelativeVirtualPath = "/" };
            bool variablesReplaced;
            var transformedVariables = TransformVariables(args.Result.FirstPart, out variablesReplaced);
            if (variablesReplaced)
            {
                Control control = page.ParseControl(transformedVariables);
                args.Result.FirstPart = HtmlUtil.RenderControl(control);
            }
        }

        protected virtual string TransformVariables(string html, out bool variablesReplaced)
        {
            variablesReplaced = false;
            foreach (Match inputMatch in RichTextTokenSpans.Matches(html))
            {
                variablesReplaced = true;
                #region SITECORE SUPPORT 360033
                //Removed the below, which was sending the ID to GetTextVariableValue.
                //                string tokenValue = GetRichTextVariableValue(inputMatch.Groups[1].Value);
                //Added this. Which sends the entire <span> string to the SupportGetRichTextVariableValue.
                string tokenValue = SupportGetRichTextVariableValue(inputMatch.Groups[0].Value);
                #endregion
                if (PageMode.IsExperienceEditorEditing && !PageMode.IsNormal && !string.IsNullOrWhiteSpace(tokenValue))
                {
                    html = html.Replace(inputMatch.Value, tokenValue);
                }
                else
                {
                    //if we are in preview mode always replace variable input
                    html = html.Replace(inputMatch.Value, tokenValue);
                }

            }
            foreach (Match tokenStringMatch in TextTokenInputs.Matches(html))
            {
                variablesReplaced = true;

                string variableValue = GetTextVariableValue(tokenStringMatch.Groups[1].Value);

                if (!string.IsNullOrWhiteSpace(variableValue))
                {
                    html = html.Replace(tokenStringMatch.Value, variableValue);
                }
            }
            return html;
        }
        #region SITECORE SUPPORT 360033
        protected virtual string SupportGetRichTextVariableValue(string spanString)
        {
            /*Get the value between the <spans>, regex explanation below:
              
             
          \>           # Escaped parenthesis, means "starts with a '>' character"
              (        # Parentheses in a regex mean "put (capture) the stuff 
                       #     in between into the Groups array" 
                 [^)]  # Any character that is not a '>' character
                 *     # Zero or more occurrences of the aforementioned "non '<' char"
              )        # Close the capturing group
          \<           # "Ends with a '<' character"
             
             
             */
            string val = Regex.Match(spanString, @"\>([^)]*)\<").Groups[1].Value;
            //Get the SXA Site item
            var siteItem = ServiceLocator.ServiceProvider.GetService<IMultisiteContext>().GetSiteItem(Context.Item);
            //Get the Content Tokens data folder
            var tokens = siteItem?.Children["Data"]?.Children["Content Tokens"];
            //Find the first child of the tokens data folder where the key field matches the ket specified above. Then get the value of the Value field of that child and convert it to string.
            string variable = tokens.Children.First(c => c.Fields["Key"].Value == val).Fields["Value"].ToString();
            //If we found the value, return it, otherwise, return empty string.
            if (variable != null)
            {
                return variable;
            }
            return string.Empty;
        }
        #endregion
        protected virtual string GetRichTextVariableValue(string variableId)
        {
            if (ID.IsID(variableId))
            {
                Item variable = Context.Database.GetItem(new ID(variableId));
                if (variable != null)
                {
                    return variable[Sitecore.XA.Feature.ContentTokens.Templates.ContentToken.Fields.Value];
                }
            }
            return string.Empty;
        }

        protected virtual string GetTextVariableValue(string variableKey)
        {
            if (!string.IsNullOrWhiteSpace(variableKey))
            {
                Item variable = Context.Database.SelectSingleItem($"fast://*[@@templateid='{Sitecore.XA.Feature.ContentTokens.Templates.ContentToken.ID}' and @#Key#='{variableKey}']");
                if (variable != null)
                {
                    return variable[Sitecore.XA.Feature.ContentTokens.Templates.ContentToken.Fields.Value];
                }
            }
            return string.Empty;
        }
    }

}