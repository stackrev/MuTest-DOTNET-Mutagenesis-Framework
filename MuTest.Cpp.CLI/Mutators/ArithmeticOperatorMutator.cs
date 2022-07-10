﻿using System.Collections.Generic;
using MuTest.Core.Mutators;

namespace MuTest.Cpp.CLI.Mutators
{
    internal class ArithmeticOperatorMutator : Mutator, IMutator
    {
        public ArithmeticOperatorMutator()
        {
            KindsToMutate = new Dictionary<string, IList<string>>
            {
                [" \\+ "] = new List<string> { "RL" },
                ["-"] = new List<string> { "+" },
                [" \\* "] = new List<string> { " / " },
                [" \\/ "] = new List<string> { " * " },
            };
        }

        public override MutatorType MutatorType { get; } = MutatorType.Arithmetic;

        public string Description { get; } = "ARITHMETIC [+, -, x, %]";

        public bool DefaultMutant { get; } = true;
    }
}