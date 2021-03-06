﻿using D2L.CodeStyle.TestAnalyzers.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace D2L.CodeStyle.TestAnalyzers.NUnit {
    [DiagnosticAnalyzer( LanguageNames.CSharp )]
    internal sealed class TestAttributeAnalyzer : DiagnosticAnalyzer {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
            Diagnostics.TestAttributeMissed
        );

        private const string AllowedListFileName = "TestAttributeAnalyzerDisallowedList.txt";

        public override void Initialize( AnalysisContext context ) {
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction( OnCompilationStart );
        }

        private static void OnCompilationStart(
            CompilationStartAnalysisContext context
        ) {
            if ( !TryLoadNUnitTypes( context.Compilation, out NUnitTypes types ) ) {
                return;
            }

            ImmutableHashSet<string> allowedClasses = GetAllowedList(
                context.Options.AdditionalFiles
            );

            context.RegisterSyntaxNodeAction(
                ctx => AnalyzeMethod(
                    context: ctx,
                    types: types,
                    syntax: ctx.Node as MethodDeclarationSyntax,
                    disallowedList: allowedClasses
                ),
                SyntaxKind.MethodDeclaration
            );
        }

        private static void AnalyzeMethod(
            SyntaxNodeAnalysisContext context,
            NUnitTypes types,
            MethodDeclarationSyntax syntax,
            ImmutableHashSet<string> disallowedList
        ) {
            SemanticModel model = context.SemanticModel;

            IMethodSymbol method = model.GetDeclaredSymbol( syntax, context.CancellationToken );
            if ( method == null ) {
                return;
            }

            // Any private/helper methods should be private/internal and can be ignored
            if ( method.DeclaredAccessibility != Accessibility.Public ) {
                return;
            }

            // We need the declaring class to be a [TestFixture] to continue
            INamedTypeSymbol declaringClass = method.ContainingType;
            if ( !declaringClass.GetAttributes().Any( attr => attr.AttributeClass.Equals( types.TestFixtureAttribute ) ) ) {
                return;
            }

            // Ignore any classes which are disallowed
            if( IsClassAllowed( disallowedList, method.ContainingType ) ) {
                return;
            }

            bool isTest = IsTestMethod( types, method );
            if ( !isTest ) {
                context.ReportDiagnostic( Diagnostic.Create(
                        Diagnostics.TestAttributeMissed, syntax.Identifier.GetLocation(), method.Name )
                    );
                return;
            }
        }

        private static bool IsTestMethod(
            NUnitTypes types,
            IMethodSymbol method
        ) {
            foreach ( AttributeData attribute in method.GetAttributes() ) {
                INamedTypeSymbol attributeType = attribute.AttributeClass;
                if ( types.TestAttributes.Contains( attributeType ) || types.SetupTeardownAttributes.Contains( attributeType ) ) {
                    return true;
                }
            }

            return false;
        }

        private static bool TryLoadNUnitTypes(
            Compilation compilation,
            out NUnitTypes types
        ) {
            ImmutableHashSet<INamedTypeSymbol> testAttributes = ImmutableHashSet
                .Create(
                    compilation.GetTypeByMetadataName( "NUnit.Framework.TestAttribute" ),
                    compilation.GetTypeByMetadataName( "NUnit.Framework.TestCaseAttribute" ),
                    compilation.GetTypeByMetadataName( "NUnit.Framework.TestCaseSourceAttribute" ),
                    compilation.GetTypeByMetadataName( "NUnit.Framework.TheoryAttribute" )
                );
            ImmutableHashSet<INamedTypeSymbol> setupTeardownAttributes = ImmutableHashSet
                .Create(
                    compilation.GetTypeByMetadataName( "NUnit.Framework.SetUpAttribute" ),
                    compilation.GetTypeByMetadataName( "NUnit.Framework.OneTimeSetUpAttribute" ),
                    compilation.GetTypeByMetadataName( "NUnit.Framework.TearDownAttribute" ),
                    compilation.GetTypeByMetadataName( "NUnit.Framework.OneTimeTearDownAttribute" )
                );

            INamedTypeSymbol testFixtureAttribute = compilation.GetTypeByMetadataName( "NUnit.Framework.TestFixtureAttribute" );

            types = new NUnitTypes( testAttributes, setupTeardownAttributes, testFixtureAttribute );
            return true;
        }

        private static bool IsClassAllowed(
            ImmutableHashSet<string> allowedClasses,
            ISymbol classSymbol
        ) {
            bool isAllowed = allowedClasses.Contains( GetAllowedListName( classSymbol ) );

            return isAllowed;
        }

        private static string GetAllowedListName( ISymbol classSymbol ) =>
            classSymbol.ToString()
            + ", "
            + classSymbol.ContainingAssembly.ToDisplayString( SymbolDisplayFormat.MinimallyQualifiedFormat )
        ;

        private static ImmutableHashSet<string> GetAllowedList(
            ImmutableArray<AdditionalText> additionalFiles
        ) {
            ImmutableHashSet<string>.Builder allowedClasses = ImmutableHashSet.CreateBuilder<string>();

            AdditionalText allowedListFile = additionalFiles.FirstOrDefault(
                file => Path.GetFileName( file.Path ) == AllowedListFileName
            );

            if( allowedListFile == null ) {
                return allowedClasses.ToImmutableHashSet();
            }

            SourceText allowedListText = allowedListFile.GetText();

            foreach( TextLine line in allowedListText.Lines ) {
				allowedClasses.Add( line.ToString().Trim() );
            }

            return allowedClasses.ToImmutableHashSet();
        }

        private sealed class NUnitTypes {

            internal NUnitTypes(
                ImmutableHashSet<INamedTypeSymbol> testAttributes,
                ImmutableHashSet<INamedTypeSymbol> setupTeardownAttributes,
                INamedTypeSymbol testFixtureAttribute
            ) {
                TestAttributes = testAttributes;
                SetupTeardownAttributes = setupTeardownAttributes;
                TestFixtureAttribute = testFixtureAttribute;
            }

            public ImmutableHashSet<INamedTypeSymbol> TestAttributes { get; }
            public INamedTypeSymbol TestFixtureAttribute { get; }
            public ImmutableHashSet<INamedTypeSymbol> SetupTeardownAttributes { get; }
        }
    }
}
