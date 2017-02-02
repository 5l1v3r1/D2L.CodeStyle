﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;

namespace D2L.CodeStyle.TestAnalyzers.Test.Verifiers {
	/// <summary>
	/// Class for turning strings into documents and getting the diagnostics on them
	/// All methods are static
	/// </summary>
	public abstract partial class DiagnosticVerifier {
		private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile( typeof( object ).Assembly.Location );
		private static readonly MetadataReference SystemCoreReference = MetadataReference.CreateFromFile( typeof( Enumerable ).Assembly.Location );
		private static readonly MetadataReference CSharpSymbolsReference = MetadataReference.CreateFromFile( typeof( CSharpCompilation ).Assembly.Location );
		private static readonly MetadataReference CodeAnalysisReference = MetadataReference.CreateFromFile( typeof( Compilation ).Assembly.Location );
		private static readonly MetadataReference ImmutableCollectionsReference = MetadataReference.CreateFromFile( typeof( ImmutableArray ).Assembly.Location );

		internal static string DefaultFilePathPrefix = "Test";
		internal static string CSharpDefaultFileExt = "cs";
		internal static string VisualBasicDefaultExt = "vb";
		internal static string TestProjectName = "StubProject";

		#region  Get Diagnostics

		/// <summary>
		/// Given classes in the form of strings, their language, and an IDiagnosticAnlayzer to apply to it, return the diagnostics found in the string after converting it to a document.
		/// </summary>
		/// <param name="sources">Classes in the form of strings</param>
		/// <param name="language">The language the source classes are in</param>
		/// <param name="analyzer">The analyzer to be run on the sources</param>
		/// <returns>An IEnumerable of Diagnostics that surfaced in the source code, sorted by Location</returns>
		private Diagnostic[] GetSortedDiagnostics( string[] sources, string language, DiagnosticAnalyzer analyzer ) {
			return GetSortedDiagnosticsFromDocuments( analyzer, GetDocuments( sources, language ) );
		}

		/// <summary>
		/// Given an analyzer and a document to apply it to, run the analyzer and gather an array of diagnostics found in it.
		/// The returned diagnostics are then ordered by location in the source document.
		/// </summary>
		/// <param name="analyzer">The analyzer to run on the documents</param>
		/// <param name="documents">The Documents that the analyzer will be run on</param>
		/// <returns>An IEnumerable of Diagnostics that surfaced in the source code, sorted by Location</returns>
		protected Diagnostic[] GetSortedDiagnosticsFromDocuments( DiagnosticAnalyzer analyzer, Document[] documents ) {
			var projects = new HashSet<Project>();
			foreach( var document in documents ) {
				projects.Add( document.Project );
			}

			var diagnostics = new List<Diagnostic>();
			foreach( var project in projects ) {
				var compilationWithAnalyzers = project.GetCompilationAsync().Result.WithAnalyzers( ImmutableArray.Create( analyzer ) );
				var diags = compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync().Result;
				foreach( var diag in diags ) {
					if( diag.Location == Location.None || diag.Location.IsInMetadata ) {
						diagnostics.Add( diag );
					} else {
						for( int i = 0; i < documents.Length; i++ ) {
							var document = documents[i];
							var tree = document.GetSyntaxTreeAsync().Result;
							if( tree == diag.Location.SourceTree ) {
								diagnostics.Add( diag );
							}
						}
					}
				}
			}

			var results = SortDiagnostics( diagnostics );
			diagnostics.Clear();
			return results;
		}

		/// <summary>
		/// Sort diagnostics by location in source document
		/// </summary>
		/// <param name="diagnostics">The list of Diagnostics to be sorted</param>
		/// <returns>An IEnumerable containing the Diagnostics in order of Location</returns>
		private Diagnostic[] SortDiagnostics( IEnumerable<Diagnostic> diagnostics ) {
			return diagnostics.OrderBy( d => d.Location.SourceSpan.Start ).ToArray();
		}

		#endregion

		#region Set up compilation and documents
		/// <summary>
		/// Given an array of strings as sources and a language, turn them into a project and return the documents and spans of it.
		/// </summary>
		/// <param name="sources">Classes in the form of strings</param>
		/// <param name="language">The language the source code is in</param>
		/// <returns>A Tuple containing the Documents produced from the sources and their TextSpans if relevant</returns>
		private Document[] GetDocuments( string[] sources, string language ) {
			if( language != LanguageNames.CSharp && language != LanguageNames.VisualBasic ) {
				throw new ArgumentException( "Unsupported Language" );
			}

			var project = CreateProject( sources, language );
			var documents = project.Documents.ToArray();

			if( sources.Length != documents.Length ) {
				throw new SystemException( "Amount of sources did not match amount of Documents created" );
			}

			return documents;
		}

		/// <summary>
		/// Create a Document from a string through creating a project that contains it.
		/// </summary>
		/// <param name="source">Classes in the form of a string</param>
		/// <param name="language">The language the source code is in</param>
		/// <returns>A Document created from the source string</returns>
		protected Document CreateDocument( string source, string language = LanguageNames.CSharp ) {
			return CreateProject( new[] { source }, language ).Documents.First();
		}

		/// <summary>
		/// Create a project using the inputted strings as sources.
		/// </summary>
		/// <param name="sources">Classes in the form of strings</param>
		/// <param name="language">The language the source code is in</param>
		/// <returns>A Project created out of the Documents created from the source strings</returns>
		private Project CreateProject( string[] sources, string language = LanguageNames.CSharp ) {
			string fileNamePrefix = DefaultFilePathPrefix;
			string fileExt = language == LanguageNames.CSharp ? CSharpDefaultFileExt : VisualBasicDefaultExt;

			var projectId = ProjectId.CreateNewId( debugName: TestProjectName );

			var solution = new AdhocWorkspace()
				.CurrentSolution
				.AddProject( projectId, TestProjectName, TestProjectName, language )
				.AddMetadataReference( projectId, CorlibReference )
				.AddMetadataReference( projectId, SystemCoreReference )
				.AddMetadataReference( projectId, CSharpSymbolsReference )
				.AddMetadataReference( projectId, CodeAnalysisReference )
				.AddMetadataReference( projectId, ImmutableCollectionsReference );

			var additionalReferences = this.GetAdditionalReferences();
			if( additionalReferences != null ) {
				solution = solution.AddMetadataReferences( projectId, additionalReferences );
			}

			int count = 0;
			foreach( var source in sources ) {
				var newFileName = fileNamePrefix + count + "." + fileExt;
				var documentId = DocumentId.CreateNewId( projectId, debugName: newFileName );
				solution = solution.AddDocument( documentId, newFileName, SourceText.From( source ) );
				count++;
			}
			return solution.GetProject( projectId );
		}
		#endregion
	}

	public abstract partial class DiagnosticVerifier {

		#region To be implemented by Test classes
		/// <summary>
		/// Get the CSharp analyzer being tested - to be implemented in non-abstract class
		/// </summary>
		protected abstract DiagnosticAnalyzer GetCSharpDiagnosticAnalyzer();

		protected virtual MetadataReference[] GetAdditionalReferences() {
			return new MetadataReference[0];
		}
		#endregion

		#region Verifier wrappers

		/// <summary>
		/// Called to test a C# DiagnosticAnalyzer when applied on the single inputted string as a source
		/// Note: input a DiagnosticResult for each Diagnostic expected
		/// </summary>
		/// <param name="source">A class in the form of a string to run the analyzer on</param>
		/// <param name="expected"> DiagnosticResults that should appear after the analyzer is run on the source</param>
		protected void VerifyCSharpDiagnostic( string source, params DiagnosticResult[] expected ) {
			VerifyDiagnostics( new[] { source }, LanguageNames.CSharp, GetCSharpDiagnosticAnalyzer(), expected );
		}

		/// <summary>
		/// Called to test a C# DiagnosticAnalyzer when applied on the inputted strings as a source
		/// Note: input a DiagnosticResult for each Diagnostic expected
		/// </summary>
		/// <param name="sources">An array of strings to create source documents from to run the analyzers on</param>
		/// <param name="expected">DiagnosticResults that should appear after the analyzer is run on the sources</param>
		protected void VerifyCSharpDiagnostic( string[] sources, params DiagnosticResult[] expected ) {
			VerifyDiagnostics( sources, LanguageNames.CSharp, GetCSharpDiagnosticAnalyzer(), expected );
		}

		/// <summary>
		/// General method that gets a collection of actual diagnostics found in the source after the analyzer is run, 
		/// then verifies each of them.
		/// </summary>
		/// <param name="sources">An array of strings to create source documents from to run the analyzers on</param>
		/// <param name="language">The language of the classes represented by the source strings</param>
		/// <param name="analyzer">The analyzer to be run on the source code</param>
		/// <param name="expected">DiagnosticResults that should appear after the analyzer is run on the sources</param>
		private void VerifyDiagnostics( string[] sources, string language, DiagnosticAnalyzer analyzer, params DiagnosticResult[] expected ) {
			var diagnostics = GetSortedDiagnostics( sources, language, analyzer );
			VerifyDiagnosticResults( diagnostics, analyzer, expected );
		}

		#endregion

		#region Actual comparisons and verifications
		/// <summary>
		/// Checks each of the actual Diagnostics found and compares them with the corresponding DiagnosticResult in the array of expected results.
		/// Diagnostics are considered equal only if the DiagnosticResultLocation, Id, Severity, and Message of the DiagnosticResult match the actual diagnostic.
		/// </summary>
		/// <param name="actualResults">The Diagnostics found by the compiler after running the analyzer on the source code</param>
		/// <param name="analyzer">The analyzer that was being run on the sources</param>
		/// <param name="expectedResults">Diagnostic Results that should have appeared in the code</param>
		private static void VerifyDiagnosticResults( IEnumerable<Diagnostic> actualResults, DiagnosticAnalyzer analyzer, params DiagnosticResult[] expectedResults ) {
			int expectedCount = expectedResults.Count();
			int actualCount = actualResults.Count();

			if( expectedCount != actualCount ) {
				string diagnosticsOutput = actualResults.Any() ? FormatDiagnostics( analyzer, actualResults.ToArray() ) : "    NONE.";

				Assert.IsTrue( false,
					$"Mismatch between number of diagnostics returned, expected \"{expectedCount}\" actual \"{actualCount}\"\r\n\r\nDiagnostics:\r\n{diagnosticsOutput}\r\n" );
			}

			for( int i = 0; i < expectedResults.Length; i++ ) {
				var actual = actualResults.ElementAt( i );
				var expected = expectedResults[i];

				if( expected.Line == -1 && expected.Column == -1 ) {
					if( actual.Location != Location.None ) {
						Assert.IsTrue( false,
							$"Expected:\nA project diagnostic with No location\nActual:\n{FormatDiagnostics( analyzer, actual )}" );
					}
				} else {
					VerifyDiagnosticLocation( analyzer, actual, actual.Location, expected.Locations.First() );
					var additionalLocations = actual.AdditionalLocations.ToArray();

					if( additionalLocations.Length != expected.Locations.Length - 1 ) {
						Assert.IsTrue( false,
							$"Expected {expected.Locations.Length - 1} additional locations but got {additionalLocations.Length} for Diagnostic:\r\n    {FormatDiagnostics( analyzer, actual )}\r\n" );
					}

					for( int j = 0; j < additionalLocations.Length; ++j ) {
						VerifyDiagnosticLocation( analyzer, actual, additionalLocations[j], expected.Locations[j + 1] );
					}
				}

				if( actual.Id != expected.Id ) {
					Assert.IsTrue( false,
						$"Expected diagnostic id to be \"{expected.Id}\" was \"{actual.Id}\"\r\n\r\nDiagnostic:\r\n    {FormatDiagnostics( analyzer, actual )}\r\n" );
				}

				if( actual.Severity != expected.Severity ) {
					Assert.IsTrue( false,
						$"Expected diagnostic severity to be \"{expected.Severity}\" was \"{actual.Severity}\"\r\n\r\nDiagnostic:\r\n    {FormatDiagnostics( analyzer, actual )}\r\n" );
				}

				if( actual.GetMessage() != expected.Message ) {
					Assert.IsTrue( false,
						$"Expected diagnostic message to be \"{expected.Message}\" was \"{actual.GetMessage()}\"\r\n\r\nDiagnostic:\r\n    {FormatDiagnostics( analyzer, actual )}\r\n" );
				}
			}
		}

		/// <summary>
		/// Helper method to VerifyDiagnosticResult that checks the location of a diagnostic and compares it with the location in the expected DiagnosticResult.
		/// </summary>
		/// <param name="analyzer">The analyzer that was being run on the sources</param>
		/// <param name="diagnostic">The diagnostic that was found in the code</param>
		/// <param name="actual">The Location of the Diagnostic found in the code</param>
		/// <param name="expected">The DiagnosticResultLocation that should have been found</param>
		private static void VerifyDiagnosticLocation( DiagnosticAnalyzer analyzer, Diagnostic diagnostic, Location actual, DiagnosticResultLocation expected ) {
			var actualSpan = actual.GetLineSpan();

			Assert.IsTrue( actualSpan.Path == expected.Path || ( actualSpan.Path != null && actualSpan.Path.Contains( "Test0." ) && expected.Path.Contains( "Test." ) ),
				$"Expected diagnostic to be in file \"{expected.Path}\" was actually in file \"{actualSpan.Path}\"\r\n\r\nDiagnostic:\r\n    {FormatDiagnostics( analyzer, diagnostic )}\r\n" );

			var actualLinePosition = actualSpan.StartLinePosition;

			// Only check line position if there is an actual line in the real diagnostic
			if( actualLinePosition.Line > 0 ) {
				if( actualLinePosition.Line + 1 != expected.Line ) {
					Assert.IsTrue( false,
						$"Expected diagnostic to be on line \"{expected.Line}\" was actually on line \"{actualLinePosition.Line + 1}\"\r\n\r\nDiagnostic:\r\n    {FormatDiagnostics( analyzer, diagnostic )}\r\n" );
				}
			}

			// Only check column position if there is an actual column position in the real diagnostic
			if( actualLinePosition.Character > 0 ) {
				if( actualLinePosition.Character + 1 != expected.Column ) {
					Assert.IsTrue( false,
						$"Expected diagnostic to start at column \"{expected.Column}\" was actually at column \"{actualLinePosition.Character + 1}\"\r\n\r\nDiagnostic:\r\n    {FormatDiagnostics( analyzer, diagnostic )}\r\n" );
				}
			}
		}
		#endregion

		#region Formatting Diagnostics
		/// <summary>
		/// Helper method to format a Diagnostic into an easily readable string
		/// </summary>
		/// <param name="analyzer">The analyzer that this verifier tests</param>
		/// <param name="diagnostics">The Diagnostics to be formatted</param>
		/// <returns>The Diagnostics formatted as a string</returns>
		private static string FormatDiagnostics( DiagnosticAnalyzer analyzer, params Diagnostic[] diagnostics ) {
			var builder = new StringBuilder();
			for( int i = 0; i < diagnostics.Length; ++i ) {
				builder.AppendLine( "// " + diagnostics[i].ToString() );

				var analyzerType = analyzer.GetType();
				var rules = analyzer.SupportedDiagnostics;

				foreach( var rule in rules ) {
					if( rule != null && rule.Id == diagnostics[i].Id ) {
						var location = diagnostics[i].Location;
						if( location == Location.None ) {
							builder.AppendFormat( "GetGlobalResult({0}.{1})", analyzerType.Name, rule.Id );
						} else {
							Assert.IsTrue( location.IsInSource,
								$"Test base does not currently handle diagnostics in metadata locations. Diagnostic in metadata: {diagnostics[i]}\r\n" );

							string resultMethodName = diagnostics[i].Location.SourceTree.FilePath.EndsWith( ".cs" ) ? "GetCSharpResultAt" : "GetBasicResultAt";
							var linePosition = diagnostics[i].Location.GetLineSpan().StartLinePosition;

							builder.AppendFormat( "{0}({1}, {2}, {3}.{4})",
								resultMethodName,
								linePosition.Line + 1,
								linePosition.Character + 1,
								analyzerType.Name,
								rule.Id );
						}

						if( i != diagnostics.Length - 1 ) {
							builder.Append( ',' );
						}

						builder.AppendLine();
						break;
					}
				}
			}
			return builder.ToString();
		}
		#endregion

	}


}

