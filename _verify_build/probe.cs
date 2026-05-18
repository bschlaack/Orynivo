using Player.Library;
using System;
using System.Linq;
using var db=AudioDatabase.OpenDefault();
var all=db.GetAll().ToList();
Console.WriteLine("formats="+string.Join(", ", all.GroupBy(t=>t.Format??"").OrderByDescending(g=>g.Count()).Select(g=>$"{g.Key}:{g.Count()}").Take(20)));
Console.WriteLine("bitrates="+string.Join(", ", all.Where(t=>t.Bitrate!=null).GroupBy(t=>t.Bitrate).OrderBy(g=>g.Key).Select(g=>$"{g.Key}:{g.Count()}").Take(30)));
Console.WriteLine("genres="+string.Join(", ", all.Where(t=>!string.IsNullOrWhiteSpace(t.Genre)).SelectMany(t=>(t.Genre??"").Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)).GroupBy(x=>x).OrderByDescending(g=>g.Count()).Select(g=>$"{g.Key}:{g.Count()}").Take(20)));
