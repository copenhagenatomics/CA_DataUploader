﻿using CA_DataUploaderLib.IOconf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace UnitTests
{
    [TestClass]
    public class IOconfMapTests
    {
        [DataRow("0_cannot_start_with_number")]
        [DataRow("æøå")]
        [DataRow("hat^")]
        [DataRow("pipe|")]
        [DataRow("back_\\slash")]
        [DataRow("forward_/slash")]
        [DataRow("ampersand&")]
        [DataRow("question?")]
        [DataRow("colon:")]
        [DataRow("exclamation!")]
        [DataRow("half½")]
        [DataRow("paragraph§")]
        [DataRow("turtle¤")]
        [DataRow("hash#tag")]
        [DataRow("percent_%rel")]
        [DataRow("angle<bracket>")]
        [DataRow("curly{bracket}")]
        [DataRow("square[bracket]")]
        [DataRow("name with space")]
        [DataRow("name_with(parenthesis)")]
        [DataRow("name~with~tilde")]
        [DataRow("name*with*star")]
        [DataRow("name,with,comma")]
        [DataRow("name.with.dot")]
        [DataRow("name=with=equals")]
        [DataRow("name+with+plus")]
        [DataRow("name-with-dash")]
        [DataTestMethod]
        public void InvalidName(string name)
        {
            var ex = Assert.ThrowsException<Exception>(() => new IOconfMap($"Map; anything_goes_here; {name}", 0));
            Assert.IsTrue(ex.Message.StartsWith($"Invalid map name: {name}"), ex.Message);
        }

        [DataRow("name_with_number_42")]
        [DataRow("UPPERCASE")]
        [DataRow("lowercase")]
        [DataRow("_name_starting_with_underscore")]
        [DataRow("name_with_underscore")]
        [DataTestMethod]
        public void ValidName(string name)
        {
            _ = new IOconfMap($"Map; anything_goes_here; {name}", 0);
        }
    }
}
