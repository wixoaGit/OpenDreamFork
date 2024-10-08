﻿using DMCompiler.Bytecode;
using DMCompiler.Compiler;
using DMCompiler.Json;

namespace DMCompiler.DM;

/// <remarks>
/// This doesn't represent a particular, specific instance of an object, <br/>
/// but rather stores the compile-time information necessary to describe a certain object definition, <br/>
/// including its procs, vars, path, parent, etc.
/// </remarks>
internal sealed class DMObject {
    public int Id;
    public DreamPath Path;
    public DMObject? Parent;
    public Dictionary<string, List<int>> Procs = new();
    public Dictionary<string, DMVariable> Variables = new();
    /// <summary> It's OK if the override var is not literally the exact same object as what it overrides. </summary>
    public Dictionary<string, DMVariable> VariableOverrides = new();
    public Dictionary<string, int> GlobalVariables = new();
    /// <summary>A list of var and verb initializations implicitly done before the user's New() is called.</summary>
    public HashSet<string> ConstVariables = new();
    public HashSet<string> TmpVariables = new();
    public List<DMExpression> InitializationProcExpressions = new();
    public int? InitializationProc;

    public bool IsRoot => Path == DreamPath.Root;

    private List<DMProc>? _verbs;

    public DMObject(int id, DreamPath path, DMObject? parent) {
        Id = id;
        Path = path;
        Parent = parent;
    }

    public void AddProc(string name, DMProc proc) {
        if (!Procs.ContainsKey(name)) Procs.Add(name, new List<int>(1));

        Procs[name].Add(proc.Id);
    }

    ///<remarks>
    /// Note that this DOES NOT query our <see cref= "GlobalVariables" />. <br/>
    /// <see langword="TODO:"/> Make this (and other things) match the nomenclature of <see cref="HasLocalVariable"/>
    /// </remarks>
    public DMVariable? GetVariable(string name) {
        if (Variables.TryGetValue(name, out var variable))
            return variable;
        if (VariableOverrides.TryGetValue(name, out variable))
             return variable;

        return Parent?.GetVariable(name);
    }

    /// <summary>
    /// Does a recursive search through self and parents to check if we already contain this variable, as a NON-STATIC VALUE!
    /// </summary>
    public bool HasLocalVariable(string name) {
        if (Variables.ContainsKey(name))
            return true;
        if (Parent == null)
            return false;
        return Parent.HasLocalVariable(name);
    }

    /// <summary> Similar to <see cref="HasLocalVariable"/>, just checks our globals/statics instead. </summary>
    /// <remarks> Does NOT return true if the global variable is in the root namespace, unless called on the Root object itself.</remarks>
    public bool HasGlobalVariable(string name) {
        if (IsRoot)
            return GlobalVariables.ContainsKey(name);
        return HasGlobalVariableNotInRoot(name);
    }

    private bool HasGlobalVariableNotInRoot(string name) {
        if (GlobalVariables.ContainsKey(name))
            return true;
        if (Parent == null || Parent.IsRoot)
            return false;
        return Parent.HasGlobalVariable(name);
    }

    public bool HasProc(string name) {
        if (Procs.ContainsKey(name)) return true;

        return Parent?.HasProc(name) ?? false;
    }

    public bool HasProcNoInheritance(string name) {
        return Procs.ContainsKey(name);
    }

    public List<int>? GetProcs(string name) {
        return Procs.GetValueOrDefault(name) ?? Parent?.GetProcs(name);
    }

    public DMComplexValueType? GetProcReturnTypes(string name) {
        if (this == DMObjectTree.Root && DMObjectTree.TryGetGlobalProc(name, out var globalProc))
            return globalProc.RawReturnTypes;
        if (GetProcs(name) is not { } procs)
            return Parent?.GetProcReturnTypes(name);

        var proc = DMObjectTree.AllProcs[procs[0]];
        if ((proc.Attributes & ProcAttributes.IsOverride) != 0)
            return Parent?.GetProcReturnTypes(name) ?? DMValueType.Anything;

        return proc.RawReturnTypes;
    }

    public void AddVerb(DMProc verb) {
        _verbs ??= new();
        _verbs.Add(verb);
    }

    public DMVariable CreateGlobalVariable(DreamPath? type, string name, bool isConst, DMComplexValueType? valType = null) {
        int id = DMObjectTree.CreateGlobal(out DMVariable global, type, name, isConst, valType ?? DMValueType.Anything);

        GlobalVariables[name] = id;
        return global;
    }

    /// <summary>
    /// Recursively searches for a global/static with the given name.
    /// </summary>
    /// <returns>Either the ID or null if no such global exists.</returns>
    public int? GetGlobalVariableId(string name) {
        if (GlobalVariables.TryGetValue(name, out int id)) {
            return id;
        }

        return Parent?.GetGlobalVariableId(name);
    }

    public DMVariable? GetGlobalVariable(string name) {
        int? id = GetGlobalVariableId(name);

        return (id == null) ? null : DMObjectTree.Globals[id.Value];
    }

    public DMComplexValueType GetReturnType(string name) {
        var procId = GetProcs(name)?[^1];

        return procId is null ? DMValueType.Anything : DMObjectTree.AllProcs[procId.Value].ReturnTypes;
    }

    public void CreateInitializationProc() {
        if (InitializationProcExpressions.Count <= 0 || InitializationProc != null)
            return;

        var init = DMObjectTree.CreateDMProc(this, null);
        InitializationProc = init.Id;
        init.Call(DMReference.SuperProc, DMCallArgumentsType.None, 0);

        foreach (DMExpression expression in InitializationProcExpressions) {
            init.DebugSource(expression.Location);
            expression.EmitPushValue(this, init);
        }
    }

    public DreamTypeJson CreateJsonRepresentation() {
        DreamTypeJson typeJson = new DreamTypeJson {
            Path = Path.PathString,
            Parent = Parent?.Id
        };

        if (Variables.Count > 0 || VariableOverrides.Count > 0) {
            typeJson.Variables = new Dictionary<string, object>();

            foreach (KeyValuePair<string, DMVariable> variable in Variables) {
                if (!variable.Value.TryAsJsonRepresentation(out var valueJson))
                    throw new Exception($"Failed to serialize {Path}.{variable.Key}");

                typeJson.Variables.Add(variable.Key, valueJson);
            }

            foreach (KeyValuePair<string, DMVariable> variable in VariableOverrides) {
                if (!variable.Value.TryAsJsonRepresentation(out var valueJson))
                    throw new Exception($"Failed to serialize {Path}.{variable.Key}");

                typeJson.Variables[variable.Key] = valueJson;
            }
        }

        if (GlobalVariables.Count > 0) {
            typeJson.GlobalVariables = GlobalVariables;
        }

        if (ConstVariables.Count > 0) {
            typeJson.ConstVariables = ConstVariables;
        }

        if (TmpVariables.Count > 0) {
            typeJson.TmpVariables = TmpVariables;
        }

        if (InitializationProc != null) {
            typeJson.InitProc = InitializationProc;
        }

        if (Procs.Count > 0) {
            typeJson.Procs = new List<List<int>>(Procs.Values);
        }

        if (_verbs != null) {
            typeJson.Verbs = new List<int>(_verbs.Count);

            foreach (var verb in _verbs) {
                typeJson.Verbs.Add(verb.Id);
            }
        }

        return typeJson;
    }

    public bool IsSubtypeOf(DreamPath path) {
        if (path.Equals(Path)) return true;
        return Parent != null && Parent.IsSubtypeOf(path);
    }

    public DMValueType GetDMValueType() {
        if (IsSubtypeOf(DreamPath.Mob))
            return DMValueType.Mob;
        if (IsSubtypeOf(DreamPath.Obj))
            return DMValueType.Obj;
        if (IsSubtypeOf(DreamPath.Area))
            return DMValueType.Area;

        return DMValueType.Anything;
    }
}
