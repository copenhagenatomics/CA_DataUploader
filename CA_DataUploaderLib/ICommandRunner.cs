﻿using System;
using System.Collections.Generic;

namespace CA_DataUploaderLib
{
    public interface ICommandRunner
    {
        Action AddCommand(string name, Func<List<string>, bool> func);
        bool Run(string cmdString, bool isUserCommand);
        List<string> ParseCommand(string name);
    }
}
