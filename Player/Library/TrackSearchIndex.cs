using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using IOPath = System.IO.Path;
using IODirectory = System.IO.Directory;

namespace Player.Library;

public static class TrackSearchIndex
{
    private const LuceneVersion Version = LuceneVersion.LUCENE_48;

    private static string Root => IOPath.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Player",
        "search-index");

    public static bool Exists()
        => IODirectory.Exists(Root) && IODirectory.EnumerateFiles(Root).Any();

    public static bool IsEmpty()
    {
        if (!Exists())
            return true;

        using var directory = FSDirectory.Open(Root);
        if (!DirectoryReader.IndexExists(directory))
            return true;

        using var reader = DirectoryReader.Open(directory);
        return reader.NumDocs == 0;
    }

    public static void Rebuild(IEnumerable<TrackRecord> tracks)
    {
        if (IODirectory.Exists(Root))
            IODirectory.Delete(Root, recursive: true);

        IODirectory.CreateDirectory(Root);
        using var directory = FSDirectory.Open(Root);
        using var analyzer = new StandardAnalyzer(Version);
        var config = new IndexWriterConfig(Version, analyzer);
        using var writer = new IndexWriter(directory, config);
        foreach (var track in tracks)
            writer.AddDocument(ToDocument(track));
        writer.Commit();
    }

    public static void UpdateMany(IEnumerable<TrackRecord> tracks)
    {
        IODirectory.CreateDirectory(Root);
        using var directory = FSDirectory.Open(Root);
        using var analyzer = new StandardAnalyzer(Version);
        var config = new IndexWriterConfig(Version, analyzer);
        using var writer = new IndexWriter(directory, config);
        foreach (var track in tracks)
            writer.UpdateDocument(new Term("path", track.Path), ToDocument(track));
        writer.Commit();
    }

    public static void RemoveMissingUnderRoot(string rootPath, IEnumerable<string> existingPaths)
    {
        if (!Exists())
            return;

        var existing = new HashSet<string>(existingPaths, StringComparer.OrdinalIgnoreCase);
        using var directory = FSDirectory.Open(Root);
        using var analyzer = new StandardAnalyzer(Version);
        var config = new IndexWriterConfig(Version, analyzer);
        using var writer = new IndexWriter(directory, config);
        using var reader = DirectoryReader.Open(directory);
        for (var i = 0; i < reader.MaxDoc; i++)
        {
            var doc = reader.Document(i);
            var path = doc.Get("path");
            if (string.IsNullOrWhiteSpace(path))
                continue;
            if (!path.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
                continue;
            if (existing.Contains(path))
                continue;

            writer.DeleteDocuments(new Term("path", path));
        }
        writer.Commit();
    }

    public static List<long> Search(string queryText, int maxResults = 500)
    {
        if (string.IsNullOrWhiteSpace(queryText) || !Exists())
            return [];

        using var directory = FSDirectory.Open(Root);
        using var reader = DirectoryReader.Open(directory);
        var searcher = new IndexSearcher(reader);
        using var analyzer = new StandardAnalyzer(Version);
        var parser = new QueryParser(Version, "all", analyzer)
        {
            DefaultOperator = Operator.AND
        };
        var query = parser.Parse(QueryParserBase.Escape(queryText));
        var hits = searcher.Search(query, maxResults).ScoreDocs;
        var result = new List<long>(hits.Length);
        foreach (var hit in hits)
        {
            var doc = searcher.Doc(hit.Doc);
            if (long.TryParse(doc.Get("id"), out var id))
                result.Add(id);
        }
        return result;
    }

    private static Document ToDocument(TrackRecord t)
    {
        var doc = new Document
        {
            new StringField("id", t.Id.ToString(), Field.Store.YES),
            new StringField("path", t.Path, Field.Store.YES),
            new TextField("all", BuildAllText(t), Field.Store.NO)
        };
        return doc;
    }

    private static string BuildAllText(TrackRecord t)
    {
        var values = new object?[]
        {
            t.Path, t.FileName, t.FileSize, t.ModifiedAt, t.AddedAt,
            t.Format, t.Duration, t.SampleRate, t.BitDepth, t.Channels, t.Bitrate,
            t.IsLossless, t.IsDsd, t.DsdRate, t.Title, t.SortTitle, t.Artist, t.SortArtist,
            t.AlbumArtist, t.SortAlbumArtist, t.Album, t.SortAlbum, t.Genre, t.Year, t.Date,
            t.TrackNumber, t.TrackTotal, t.DiscNumber, t.DiscTotal, t.Composer, t.Conductor,
            t.Lyricist, t.Lyrics, t.Comment, t.Copyright, t.Publisher, t.EncodedBy,
            t.EncodingSettings, t.Bpm, t.Compilation, t.Isrc, t.Language, t.Mood,
            t.ReplayGainTrack, t.ReplayGainAlbum, t.MusicBrainzTrackId, t.MusicBrainzReleaseId,
            t.MusicBrainzArtistId, t.AcoustIdFingerprint, t.HasCover, t.CoverMimeType
        };
        return string.Join(' ', values.Where(v => v is not null));
    }
}
