using System;
using System.Diagnostics;
using Player.Library;

using var db = AudioDatabase.OpenDefault();
void Time(string name, Action action)
{
    var sw = Stopwatch.StartNew();
    action();
    sw.Stop();
    Console.WriteLine($"{name}: {sw.ElapsedMilliseconds} ms");
}
Time("Artists", () => Console.WriteLine(db.GetArtistsLite().Count));
Time("Albums", () => Console.WriteLine(db.GetAlbumsLite().Count));
Time("TracksFull", () => Console.WriteLine(db.GetAll().Count()));
Time("TracksLite", () => Console.WriteLine(db.GetTracksLite().Count));
