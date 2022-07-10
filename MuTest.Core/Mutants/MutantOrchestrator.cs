﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MuTest.Core.Common;
using MuTest.Core.Model.AridNodes;
using MuTest.Core.Model.ClassDeclarations;
using MuTest.Core.Mutators;
using MuTest.Core.Utility;

namespace MuTest.Core.Mutants
{
    public interface IMutantOrchestrator
    {
        SyntaxNode Mutate(SyntaxNodeAnalysis analysis);

        IEnumerable<Mutant> GetLatestMutantBatch();
    }

    public class MutantOrchestrator : IMutantOrchestrator
    {
        private static readonly SyntaxNodeAnalysisFactory SyntaxNodeAnalysisFactory = new SyntaxNodeAnalysisFactory(); 
        public static IList<IMutator> AllMutators =>
            new List<IMutator>
            {
                new ArithmeticOperatorMutator(),
                new RelationalOperatorMutator(),
                new LogicalConnectorMutator(),
                new StatementBlockMutator(),
                new PostfixUnaryMutator(),
                new PrefixUnaryMutator(),
                new AssignmentStatementMutator(),
                new StringMutator(),
                new InterpolatedStringMutator(),
                new MethodCallMutator(),
                new BitwiseOperatorMutator(),
                new NonVoidMethodCallMutator(),
                new LinqMutator(),
                new BooleanMutator(),
                new NegateConditionMutator()
            };

        public static IList<IMutator> DefaultMutators =>
            new List<IMutator>
            {
                new ArithmeticOperatorMutator(),
                new LogicalConnectorMutator(),
                new RelationalOperatorMutator(),
                new StatementBlockMutator(),
                new PrefixUnaryMutator(),
                new PostfixUnaryMutator()
            };

        private ICollection<Mutant> Mutants { get; set; }
        private int MutantCount { get; set; }
        private IEnumerable<IMutator> Mutators { get; }

        public MutantOrchestrator(IEnumerable<IMutator> mutators = null)
        {
            Mutators = mutators ?? AllMutators;
            Mutants = new Collection<Mutant>();
        }

        public static IEnumerable<Mutant> GetDefaultMutants(SyntaxNode node, ClassDeclaration classDeclaration)
        {
            var orchestrator = new MutantOrchestrator(DefaultMutators);

            var analysis = SyntaxNodeAnalysisFactory.Create(node, classDeclaration);
            orchestrator.Mutate(analysis);

            return orchestrator.GetLatestMutantBatch();
        }

        public IEnumerable<Mutant> GetLatestMutantBatch()
        {
            var mutants = new List<Mutant>();
            foreach (var mutant in Mutants)
            {
                if (mutant.Mutation.Type != MutatorType.MethodCall)
                {
                    mutants.Add(mutant);
                    continue;
                }

                if (Mutants.Count(x => x.Mutation.Location == mutant.Mutation.Location) == 1)
                {
                    mutants.Add(mutant);
                }
            }

            var tempMutants = mutants;
            Mutants = new Collection<Mutant>();
            return tempMutants;
        }

        public SyntaxNode Mutate(SyntaxNodeAnalysis analysis)
        {
            return Mutate(analysis.Root, analysis);
        }

        private SyntaxNode Mutate(SyntaxNode currentNode, SyntaxNodeAnalysis analysis)
        {

            if (currentNode is MethodDeclarationSyntax ||
                currentNode is ConstructorDeclarationSyntax ||
                currentNode is PropertyDeclarationSyntax)
            {
                foreach (var blockNode in currentNode.DescendantNodes<BlockSyntax>())
                {
                    if (analysis.IsNodeArid(blockNode))
                    {
                        continue;
                    }
                    AddBlockMutants(blockNode);
                }
            }

            if (GetExpressionSyntax(currentNode) is var expressionSyntax 
                && expressionSyntax != null 
                && !analysis.IsNodeArid(expressionSyntax))
            {
                if (currentNode is ExpressionStatementSyntax syntax)
                {
                    if (expressionSyntax is AssignmentExpressionSyntax)
                    {
                        return MutateWithIfStatements(expressionSyntax.Parent);
                    }

                    if (GetExpressionSyntax(expressionSyntax) is var subExpressionSyntax && subExpressionSyntax != null)
                    {
                        return currentNode.ReplaceNode(expressionSyntax, Mutate(expressionSyntax, analysis));
                    }

                    return MutateWithIfStatements(syntax);
                }

                return currentNode.ReplaceNode(expressionSyntax, MutateWithConditionalExpressions(expressionSyntax));
            }

            if (currentNode is StatementSyntax statement 
                && currentNode.Kind() != SyntaxKind.Block
                && !analysis.IsNodeArid(statement))
            {
                if (currentNode is LocalFunctionStatementSyntax localFunction)
                {
                    return localFunction.ReplaceNode(localFunction.Body, Mutate(localFunction.Body, analysis));
                }

                if (currentNode is IfStatementSyntax ifStatement)
                {
                    if (!ifStatement.Statement.ChildNodes().Any())
                    {
                        return null;
                    }

                    ifStatement = ifStatement.ReplaceNode(ifStatement.Condition,
                        MutateWithConditionalExpressions(ifStatement.Condition));

                    if (ifStatement.Else != null)
                    {
                        ifStatement = ifStatement.ReplaceNode(ifStatement.Else, Mutate(ifStatement.Else, analysis));
                    }

                    try
                    {
                        if (ifStatement.Statement != null)
                        {
                            return ifStatement.ReplaceNode(ifStatement.Statement,
                                Mutate(ifStatement.Statement, analysis));
                        }
                    }
                    catch (Exception e)
                    {
                        Trace.TraceError("unable to process if statement at line {0} {1}",
                            ifStatement?.Statement?.LineNumber() + 1, e);
                    }
                }

                return MutateWithIfStatements(statement);
            }

            if (currentNode is InvocationExpressionSyntax invocationExpression 
                && invocationExpression.ArgumentList.Arguments.Count == 0
                && !analysis.IsNodeArid(invocationExpression))
            {
                var mutant = FindMutants(invocationExpression).FirstOrDefault();
                if (mutant != null)
                {
                    Mutants.Add(mutant);
                }
            }

            var children = currentNode.ChildNodes().ToList();
            foreach (var child in children)
            {
                Mutate(child, analysis);
            }

            return currentNode;
        }

        private void AddBlockMutants(SyntaxNode currentNode)
        {
            if (currentNode is StatementSyntax block && currentNode.Kind() == SyntaxKind.Block)
            {
                var mutant = FindMutantsWithoutChild(block).FirstOrDefault();
                if (mutant != null)
                {
                    Mutants.Add(mutant);
                }
            }
        }

        private IEnumerable<Mutant> FindMutants(SyntaxNode current)
        {
            foreach (var mutator in Mutators)
            {
                if (!(mutator is StatementBlockMutator))
                {
                    foreach (var mutation in ApplyMutator(current, mutator))
                    {
                        yield return mutation;
                    }
                }
            }

            foreach (var mutant in current.ChildNodes().SelectMany(FindMutants))
            {
                yield return mutant;
            }
        }

        private IEnumerable<Mutant> FindMutantsWithoutChild(SyntaxNode current)
        {
            foreach (var mutator in Mutators)
            {
                foreach (var mutation in ApplyMutator(current, mutator))
                {
                    yield return mutation;
                }
            }
        }

        private SyntaxNode MutateWithIfStatements(SyntaxNode currentNode)
        {
            var ast = currentNode;
            foreach (var mutant in currentNode.ChildNodes().SelectMany(FindMutants))
            {
                Mutants.Add(mutant);
            }

            return ast;
        }

        private SyntaxNode MutateWithConditionalExpressions(ExpressionSyntax currentNode)
        {
            ExpressionSyntax expressionAst = currentNode;
            foreach (var mutant in FindMutants(currentNode))
            {
                Mutants.Add(mutant);
            }

            return expressionAst;
        }

        private IEnumerable<Mutant> ApplyMutator(SyntaxNode syntaxNode, IMutator mutator)
        {
            var mutations = mutator.Mutate(syntaxNode);
            foreach (var mutation in mutations)
            {
                yield return new Mutant
                {
                    Id = MutantCount++,
                    Mutation = mutation,
                    ResultStatus = MutantStatus.NotRun
                };
            }
        }

        private ExpressionSyntax GetExpressionSyntax(SyntaxNode node)
        {
            switch (node.GetType().Name)
            {
                case nameof(LocalDeclarationStatementSyntax):
                    var localDeclarationStatement = node as LocalDeclarationStatementSyntax;
                    return localDeclarationStatement?.Declaration.Variables.First().Initializer?.Value;
                case nameof(AssignmentExpressionSyntax):
                    var assignmentExpression = node as AssignmentExpressionSyntax;
                    return assignmentExpression?.Right;
                case nameof(ReturnStatementSyntax):
                    var returnStatement = node as ReturnStatementSyntax;
                    return returnStatement?.Expression;
                case nameof(LocalFunctionStatementSyntax):
                    var localFunction = node as LocalFunctionStatementSyntax;
                    return localFunction?.ExpressionBody?.Expression;
                case nameof(ExpressionStatementSyntax):
                    var expressionStatement = node as ExpressionStatementSyntax;
                    return expressionStatement?.Expression;
                default:
                    return null;
            }
        }
    }
}