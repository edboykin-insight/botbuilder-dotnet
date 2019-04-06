﻿using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using Microsoft.Bot.Builder.Expressions;
using Microsoft.Bot.Builder.Expressions.Parser;

namespace Microsoft.Bot.Builder.AI.LanguageGeneration
{
    public class ReportEntry
    {
        public ReportEntryType Type { get; set; }
        public string Message { get; set; }

        public ReportEntry(string message, ReportEntryType type = ReportEntryType.ERROR)
        {
            Message = message;
            Type = type;
        }

        public override string ToString()
        {
            var label = Type == ReportEntryType.ERROR ? "[ERROR]" : "[WARN]";
            return $"{label}: {Message}";
        }
    }

    public enum ReportEntryType
    {
        ERROR,
        WARN
    }

    public class StaticChecker : LGFileParserBaseVisitor<List<ReportEntry>>
    {
        public readonly EvaluationContext Context;

        public StaticChecker(EvaluationContext context)
        {
            Context = context;
        }

        /// <summary>
        /// Return error messaages list
        /// </summary>
        /// <returns></returns>
        public List<ReportEntry> Check()
        {
            var result = new List<ReportEntry>();

            if(Context.TemplateContexts == null 
                || Context.TemplateContexts.Count == 0)
            {
                result.Add(new ReportEntry("File must have at least one template definition ",
                                                ReportEntryType.WARN));
            }
            else
            {
                foreach (var template in Context.TemplateContexts)
                {
                    result.AddRange(Visit(template.Value));
                }
            }
            

            return result;
        }

        public override List<ReportEntry> VisitTemplateDefinition([NotNull] LGFileParser.TemplateDefinitionContext context)
        {
            var result = new List<ReportEntry>();
            var templateName = context.templateNameLine().templateName().GetText();

            if (context.templateBody() == null)
            {
                result.Add(new ReportEntry($"There is no template body in template {templateName}"));
            }
            else
            {
                result.AddRange(Visit(context.templateBody()));
            }

            var parameters = context.templateNameLine().parameters();
            if (parameters != null)
            {
                if (parameters.CLOSE_PARENTHESIS() == null
                       || parameters.OPEN_PARENTHESIS() == null)
                {
                    result.Add(new ReportEntry($"parameters: {parameters.GetText()} format error"));
                }

                var invalidSeperateCharacters = parameters.INVALID_SEPERATE_CHAR();
                if(invalidSeperateCharacters != null 
                    && invalidSeperateCharacters.Length > 0)
                {
                    result.Add(new ReportEntry("Parameters for templates must be separated by comma."));
                }
            }
            return result;
        }

        public override List<ReportEntry> VisitNormalTemplateBody([NotNull] LGFileParser.NormalTemplateBodyContext context)
        {
            var result = new List<ReportEntry>();

            foreach (var templateStr in context.normalTemplateString())
            {
                var item = Visit(templateStr);
                result.AddRange(item);
            }

            return result;
        }

        public override List<ReportEntry> VisitConditionalBody([NotNull] LGFileParser.ConditionalBodyContext context)
        {
            var result = new List<ReportEntry>();

            var ifRules = context.conditionalTemplateBody().ifConditionRule();
            for (int idx = 0; idx < ifRules.Length; idx++)
            {
                // check if rules must start with if and end with else, and have elseif in middle
                var conditionLabel = ifRules[idx].ifCondition().IFELSE().GetText().ToLower();

                if (idx == 0 && !string.Equals(conditionLabel, "if:"))
                {
                    result.Add(new ReportEntry($"condition is not start with if: '{context.conditionalTemplateBody().GetText()}'", ReportEntryType.WARN));
                }

                if (idx > 0 && string.Equals(conditionLabel, "if:"))
                {
                    result.Add(new ReportEntry($"condition can't have more than one if: '{context.conditionalTemplateBody().GetText()}'"));
                }

                if (idx == ifRules.Length - 1 && !string.Equals(conditionLabel, "else:"))
                {
                    result.Add(new ReportEntry($"condition is not end with else: '{context.conditionalTemplateBody().GetText()}'", ReportEntryType.WARN));
                }

                if (0 < idx && idx < ifRules.Length-1 && !string.Equals(conditionLabel, "elseif:"))
                {
                    result.Add(new ReportEntry($"only elseif is allowed in middle of condition: '{context.conditionalTemplateBody().GetText()}'"));
                }

                // check rule should should with one and only expression
                if (conditionLabel != "else:")
                {
                    if (ifRules[idx].ifCondition().EXPRESSION().Length != 1)
                    {
                        result.Add(new ReportEntry($"if and elseif should followed by one valid expression: '{ifRules[idx].GetText()}'"));
                    }
                    else
                    { 
                        result.AddRange(CheckExpression(ifRules[idx].ifCondition().EXPRESSION(0).GetText()));
                    }
                }
                else
                {
                    if (ifRules[idx].ifCondition().EXPRESSION().Length != 0)
                    {
                        result.Add(new ReportEntry($"else should not followed by any expression: '{ifRules[idx].GetText()}'"));
                    }
                }

                result.AddRange(Visit(ifRules[idx].normalTemplateBody()));
            }

            return result;
        }

        public override List<ReportEntry> VisitNormalTemplateString([NotNull] LGFileParser.NormalTemplateStringContext context)
        {
            var result = new List<ReportEntry>();

            foreach (ITerminalNode node in context.children)
            {
                switch (node.Symbol.Type)
                {
                    case LGFileParser.ESCAPE_CHARACTER:
                        result.AddRange(CheckEscapeCharacter(node.GetText()));
                        break;
                    case LGFileParser.INVALID_ESCAPE:
                        result.Add(new ReportEntry($"escape character {node.GetText()} is invalid"));
                        break;
                    case LGFileParser.TEMPLATE_REF:
                        result.AddRange(CheckTemplateRef(node.GetText()));
                        break;
                    case LGFileParser.EXPRESSION:
                        result.AddRange(CheckExpression(node.GetText()));
                        break;
                    case LGFileLexer.MULTI_LINE_TEXT:
                        result.AddRange(CheckMultiLineText(node.GetText()));
                        break;
                    case LGFileLexer.TEXT:
                        result.AddRange(CheckText(node.GetText()));
                        break;
                    default:
                        break;
                }
            }
            return result;
        }

        public List<ReportEntry> CheckTemplateRef(string exp)
        {
            var result = new List<ReportEntry>();

            exp = exp.TrimStart('[').TrimEnd(']').Trim();

            var argsStartPos = exp.IndexOf('(');
            if (argsStartPos > 0) // Do have args
            {
                // EvaluateTemplate all arguments using ExpressoinEngine
                var argsEndPos = exp.LastIndexOf(')');
                if (argsEndPos < 0 || argsEndPos < argsStartPos + 1)
                {
                    result.Add(new ReportEntry($"Not a valid template ref: {exp}"));
                }
                else
                {
                    var templateName = exp.Substring(0, argsStartPos);
                    if (!Context.TemplateContexts.ContainsKey(templateName))
                    {
                        result.Add(new ReportEntry($"No such template: {templateName}"));
                    }
                    else
                    {
                        var argsNumber = exp.Substring(argsStartPos + 1, argsEndPos - argsStartPos - 1).Split(',').Length;
                        result.AddRange(CheckTemplateParameters(templateName, argsNumber));
                    }
                }
            }
            else
            {
                if (!Context.TemplateContexts.ContainsKey(exp))
                {
                    result.Add(new ReportEntry($"No such template: {exp}"));
                }
            }
            return result;
        }

        private List<ReportEntry> CheckMultiLineText(string exp)
        {
            var result = new List<ReportEntry>();

            exp = exp.Substring(3, exp.Length - 6); //remove ``` ```
            var reg = @"@\{[^{}]+\}";
            var mc = Regex.Matches(exp, reg);

            foreach (Match match in mc)
            {
                var newExp = match.Value.Substring(1); // remove @
                if (newExp.StartsWith("{[") && newExp.EndsWith("]}"))
                {
                    result.AddRange(CheckTemplateRef(newExp.Substring(2, newExp.Length - 4)));//[ ]
                }
            }
            return result;
        }

        private List<ReportEntry> CheckText(string exp)
        {
            var result = new List<ReportEntry>();

            if (exp.StartsWith("```"))
                result.Add(new ReportEntry("Multi line variation must be enclosed in ```"));
            return result;
        }

        private List<ReportEntry> CheckTemplateParameters(string templateName, int argsNumber)
        {
            var result = new List<ReportEntry>();
            var parametersNumber = Context.TemplateParameters.TryGetValue(templateName, out var parameters) ?
                parameters.Count : 0;

            if (argsNumber != parametersNumber)
            {
                result.Add(new ReportEntry($"Arguments count mismatch for template ref {templateName}, expected {parametersNumber}, actual {argsNumber}"));
            }

            return result;
        }

        private List<ReportEntry> CheckExpression(string exp)
        {
            var result = new List<ReportEntry>();
            exp = exp.TrimStart('{').TrimEnd('}');
            try
            {
                new ExpressionEngine(new GetMethodExtensions(null).GetMethodX).Parse(exp);
            }
            catch(Exception e)
            {
                result.Add(new ReportEntry(e.Message));
                return result;
            }

            return result;
            
        }

        private List<ReportEntry> CheckEscapeCharacter(string exp)
        {
            var result = new List<ReportEntry>();
            var ValidEscapeCharacters = new List<string> {
                @"\r", @"\n", @"\t", @"\\", @"\[", @"\]", @"\{", @"\}"
            };

            if (!ValidEscapeCharacters.Contains(exp))
                result.Add(new ReportEntry($"escape character {exp} is invalid"));

            return result;
        }
    }
}
