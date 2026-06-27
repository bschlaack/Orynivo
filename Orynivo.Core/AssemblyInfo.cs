using System.Runtime.CompilerServices;

// Grant the Orynivo Windows desktop app access to internal Core types.
// The server project (Orynivo.Server) intentionally does NOT have access
// to audio-processing internals — it serves files via HTTP, not sample data.
[assembly: InternalsVisibleTo("Orynivo")]
