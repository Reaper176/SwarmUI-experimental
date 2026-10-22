using NUnit.Framework;
using SwarmUI.Utils;
using SwarmUI.WebAPI;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System;

namespace SwarmUITests;

public class HistoryFolderExclusionsTests
{
    [TestCase("chants", true)]
    [TestCase("chants/nested/image.png", true)]
    [TestCase("chants-other/image.png", false)]
    [TestCase("other/chants/image.png", false)]
    [TestCase("visible/image.png", false)]
    [TestCase("", false)]
    public void ExcludesOnlySelectedFoldersAndDescendants(string path, bool expected)
    {
        HistoryFolderExclusions exclusions = new(["chants"]);
        Assert.That(exclusions.IsExcluded(path), Is.EqualTo(expected));
    }

    [TestCase("./renders\\drafts/", "renders/drafts/image.png", true)]
    [TestCase("renders//drafts", "./renders/drafts/child/", true)]
    [TestCase("renders/drafts", "renders/drafts/../final/image.png", false)]
    [TestCase("renders/drafts", "renders/final/image.png", false)]
    public void NormalizesSeparatorsAndDotSegments(string folder, string path, bool expected)
    {
        HistoryFolderExclusions exclusions = new([folder]);
        Assert.That(exclusions.IsExcluded(path), Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase(".")]
    [TestCase("/")]
    [TestCase("../chants")]
    public void EmptyOrEscapingEntriesDoNotHideHistory(string folder)
    {
        HistoryFolderExclusions exclusions = new([folder]);
        Assert.That(exclusions.IsExcluded("chants/image.png"), Is.False);
    }

    [Test]
    public void CaseMatchingFollowsPlatformConvention()
    {
        HistoryFolderExclusions exclusions = new(["Chants"]);
        Assert.That(exclusions.IsExcluded("chants/image.png"), Is.EqualTo(OperatingSystem.IsWindows()));
    }

    [TestCase("chants || renders/drafts")]
    [TestCase("chants,renders/drafts")]
    public void SettingsListAcceptsDisplayedAndLegacySeparators(string text)
    {
        List<string> folders = (List<string>)AdminAPI.DataToType(new JValue(text), typeof(List<string>));
        Assert.That(folders, Is.EqualTo(new[] { "chants", "renders/drafts" }));
    }

    [Test]
    public void EmptyListAndIndependentUserLists()
    {
        HistoryFolderExclusions firstUser = new(["chants", "drafts"]);
        HistoryFolderExclusions secondUser = new([]);
        Assert.That(firstUser.IsExcluded("drafts/image.png"), Is.True);
        Assert.That(secondUser.IsExcluded("drafts/image.png"), Is.False);
    }
}
