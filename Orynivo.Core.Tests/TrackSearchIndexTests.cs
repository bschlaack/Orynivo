using Lucene.Net.Analysis.De;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>Verifies multi-field full-text query behavior used by local and server libraries.</summary>
public sealed class TrackSearchIndexTests
{
    private const LuceneVersion Version = LuceneVersion.LUCENE_48;

    /// <summary>Verifies that artist and title terms may match different indexed fields.</summary>
    [Fact]
    public void BuildPartialWordQuery_MatchesArtistAndTitleAcrossFields()
    {
        using var directory = new RAMDirectory();
        using var analyzer = new GermanAnalyzer(Version);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig(Version, analyzer)))
        {
            writer.AddDocument(
            [
                new StringField("id", "1", Field.Store.YES),
                new TextField("title", "Take on Me", Field.Store.NO),
                new TextField("artist", "A-Ha", Field.Store.NO),
                new TextField("album", "Hunting High and Low", Field.Store.NO)
            ]);
            writer.AddDocument(
            [
                new StringField("id", "2", Field.Store.YES),
                new TextField("title", "Take on Me", Field.Store.NO),
                new TextField("artist", "Another Artist", Field.Store.NO),
                new TextField("album", "Compilation", Field.Store.NO)
            ]);
            writer.Commit();
        }

        using var reader = DirectoryReader.Open(directory);
        var searcher = new IndexSearcher(reader);
        var query = TrackSearchIndex.BuildPartialWordQuery(
            analyzer,
            ["title", "artist", "album"],
            "A-Ha Take on Me");

        Assert.NotNull(query);
        var hits = searcher.Search(query, 10).ScoreDocs;
        var hit = Assert.Single(hits);
        Assert.Equal("1", searcher.Doc(hit.Doc).Get("id"));
    }
}
