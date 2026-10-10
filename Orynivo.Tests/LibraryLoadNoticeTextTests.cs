using System.Reflection;
using Orynivo.Localization;
using Xunit;

namespace Orynivo.Tests;

/// <summary>Checks that incomplete load notices are localized and successful or cancelled loads remain quiet.</summary>
public sealed class LibraryLoadNoticeTextTests
{
    /// <summary>Every complete built-in language provides usable failure and retry text.</summary>
    [Fact]
    public void EveryLanguage_FormatsIncompleteLoadAndRetry()
    {
        var languages = typeof(LocalizationManager).GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => field.IsInitOnly && field.FieldType == typeof(LocalizedStrings))
            .Select(field => (LocalizedStrings)field.GetValue(null)!).ToArray();
        Assert.Equal(7, languages.Length);
        foreach (var strings in languages)
        {
            foreach (var status in new[] { LibraryLoadStatus.Partial, LibraryLoadStatus.Failed })
            {
                var text = LibraryLoadNoticeText.Format(status, 2, strings);
                Assert.False(string.IsNullOrWhiteSpace(text));
                Assert.Contains("2", text);
                Assert.DoesNotContain("{0}", text);
            }
            Assert.False(string.IsNullOrWhiteSpace(strings.LibraryLoadRetry));
            Assert.False(string.IsNullOrWhiteSpace(strings.LibraryLoadRetrying));
            Assert.NotEqual(strings.LibraryLoadRetry, strings.LibraryLoadRetrying);
        }
    }

    /// <summary>Successful empty results and navigation cancellation must not display failures.</summary>
    [Fact]
    public void CompleteAndCancelled_DoNotDisplayNotice()
    {
        Assert.Empty(LibraryLoadNoticeText.Format(LibraryLoadStatus.Complete, 0, LocalizationManager.Current));
        Assert.Empty(LibraryLoadNoticeText.Format(LibraryLoadStatus.Cancelled, 1, LocalizationManager.Current));
    }
}
