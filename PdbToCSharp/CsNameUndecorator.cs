using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace PdbToCSharp;

// This code is almost entirely copied from SharpPdb, with some changes to make its output more C# friendly.

/// <summary>
/// Implements UnDecorateSymbolName function. Implementation has started as Wine implementation clone.
/// </summary>
public static class CsNameUndecorator {
  /// <summary>
  /// The flags for how the decorated name is undecorated.
  /// </summary>
  [Flags]
  public enum Flags {
    /// <summary>
    /// [UNDNAME_COMPLETE] Enable full undecoration.
    /// </summary>
    Complete = 0x0000,

    /// <summary>
    /// [UNDNAME_NO_LEADING_UNDERSCORES] Remove leading underscores from Microsoft keywords.
    /// </summary>
    NoLeadingUnderscores = 0x0001,

    /// <summary>
    /// [UNDNAME_NO_MS_KEYWORDS] Disable expansion of Microsoft keywords.
    /// </summary>
    NoMicrosoftKeywords = 0x0002,

    /// <summary>
    /// [UNDNAME_NO_FUNCTION_RETURNS] Disable expansion of return types for primary declarations.
    /// </summary>
    NoFunctionReturns = 0x0004,

    /// <summary>
    /// [UNDNAME_NO_ALLOCATION_MODEL] Disable expansion of the declaration model.
    /// </summary>
    NoAllocationModel = 0x0008,

    /// <summary>
    /// [UNDNAME_NO_ALLOCATION_LANGUAGE] Disable expansion of the declaration language specifier.
    /// </summary>
    NoAllocationLanguage = 0x0010,

    /// <summary>
    /// [UNDNAME_NO_MS_THISTYPE] Disable expansion of Microsoft keywords on the this type for primary declaration.
    /// </summary>
    NoMicrosoftThisType = 0x0020,

    /// <summary>
    /// [UNDNAME_NO_CV_THISTYPE] Disable expansion of CodeView modifiers on the this type for primary declaration.
    /// </summary>
    NoCodeViewThisType = 0x0040,

    /// <summary>
    /// [UNDNAME_NO_THISTYPE] Disable all modifiers on the this type.
    /// </summary>
    NoThisType = 0x0060,

    /// <summary>
    /// [UNDNAME_NO_ACCESS_SPECIFIERS] Disable expansion of access specifiers for members.
    /// </summary>
    NoAccessSpecifiers = 0x0080,

    /// <summary>
    /// [UNDNAME_NO_THROW_SIGNATURES] Disable expansion of throw-signatures for functions and pointers to functions.
    /// </summary>
    NoThrowSignatures = 0x0100,

    /// <summary>
    /// [UNDNAME_NO_MEMBER_TYPE] Disable expansion of the static or virtual attribute of members.
    /// </summary>
    NoMemberType = 0x0200,

    /// <summary>
    /// [UNDNAME_NO_RETURN_UDT_MODEL] Disable expansion of the Microsoft model for user-defined type returns.
    /// </summary>
    NoReturnUdtModel = 0x0400,

    /// <summary>
    /// [UNDNAME_32_BIT_DECODE] Undecorate 32-bit decorated names.
    /// </summary>
    Decode32Bit = 0x0800,

    /// <summary>
    /// [UNDNAME_NAME_ONLY] Undecorate only the name for primary declaration. Returns [scope::]name. Does expand template parameters.
    /// </summary>
    NameOnly = 0x1000,

    /// <summary>
    /// [UNDNAME_NO_ARGUMENTS] Do not undecorate function arguments.
    /// </summary>
    NoArguments = 0x2000,

    /// <summary>
    /// [UNDNAME_NO_SPECIAL_SYMS] Do not undecorate special names, such as vtable, vcall, vector, metatype, and so on.
    /// </summary>
    NoSpecialSymbols = 0x4000,

    /// <summary>
    /// [UNDNAME_NO_COMPLEX_TYPE]
    /// </summary>
    NoComplexType = 0x8000,
  }

  /// <summary>
  /// Undecorates the specified decorated C++ symbol name.
  /// </summary>
  /// <param name="name">The decorated C++ symbol name. This name can be identified by the first character of the name, which is always a question mark (?).</param>
  /// <param name="flags">The options for how the decorated name is undecorated.</param>
  /// <returns>The undecorated name.</returns>
  public static string UnDecorateSymbolName(string name, Flags flags = Flags.Complete) {
    if ((flags & Flags.NameOnly) == Flags.NameOnly)
      flags |= Flags.NoFunctionReturns | Flags.NoAccessSpecifiers | Flags.NoMemberType | Flags.NoAllocationLanguage |
        Flags.NoComplexType;

    Parser parser = new() {
      Input = name,
      Flags = flags,
      Index = -1,
    };

    parser.Next();
    return SymbolDemangle(parser, out string? undecoratedName) ? undecoratedName : name;
  }

  private class StringArray {
    public int Start;
    public int Num;
    public List<string>? Strings;

    public void Push(string s) {
      Strings ??= [];
      if (Num == Strings.Count) {
        Strings.Add(s);
        Num++;
      }
      else
        Strings[Num++] = s;
    }

    public string? Get(int index) {
      if (Strings is null || Start + index >= Strings.Count)
        return null;

      return Strings[Start + index];
    }
  }

  private class Parser {
    public required string Input;
    public Flags Flags;
    public int Index;
    public char Current;
    public StringArray Stack = new();
    public readonly StringArray Names = new();

    public char Next() {
      if (Index + 1 < Input.Length) {
        return Current = Input[++Index];
      }

      Index = Input.Length;
      return Current = '\0';
    }

    public char Peek(int index) {
      return Index + index >= Input.Length ? '\0' : Input[Index + index];
    }

    public void Advance(int count) {
      Index += count;
      if (Index >= Input.Length) {
        Index = Input.Length;
        Current = '\0';
      }
      else
        Current = Input[Index];
    }

    public override string ToString() {
      if (Index < 0 || Index >= Input.Length)
        return "";

      return Input[Index..];
    }
  }

  private static bool SymbolDemangle(Parser parser, [NotNullWhen(true)] out string? undecoratedName) {
    undecoratedName = null;
    if ((parser.Flags & Flags.NoArguments) == Flags.NoArguments) {
      if (DemangleDataType(parser, out string? left, out string? right)) {
        undecoratedName = left + right;
        return true;
      }
    }

    // MS mangled names always begin with '?'
    if (parser.Current != '?') {
      return false;
    }

    parser.Next();

    // Then function name or operator code
    int doAfter = 0;

    switch (parser.Current) {
      case '?' when parser.Peek(1) != '$' || parser.Peek(2) == '?': {
        string? functionName = null;

        if (parser.Peek(1) == '$') {
          doAfter = 6;
          parser.Advance(2);
        }

        // C++ operator code (one character, or two if the first is '_')
        char next = parser.Next();
        switch (next) {
          case '0':
            doAfter = 1;
            break;
          case '1':
            doAfter = 2;
            break;
          case '2':
            functionName = "operator new";
            break;
          case '3':
            functionName = "operator delete";
            break;
          case '4':
            functionName = "operator=";
            break;
          case '5':
            functionName = "operator>>";
            break;
          case '6':
            functionName = "operator<<";
            break;
          case '7':
            functionName = "operator!";
            break;
          case '8':
            functionName = "operator==";
            break;
          case '9':
            functionName = "operator!=";
            break;
          case 'A':
            functionName = "operator[]";
            break;
          case 'B':
            functionName = "operator ";
            doAfter = 3;
            break;
          case 'C':
            functionName = "operator->";
            break;
          case 'D':
            functionName = "operator*";
            break;
          case 'E':
            functionName = "operator++";
            break;
          case 'F':
            functionName = "operator--";
            break;
          case 'G':
            functionName = "operator-";
            break;
          case 'H':
            functionName = "operator+";
            break;
          case 'I':
            functionName = "operator&";
            break;
          case 'J':
            functionName = "operator->*";
            break;
          case 'K':
            functionName = "operator/";
            break;
          case 'L':
            functionName = "operator%";
            break;
          case 'M':
            functionName = "operator<";
            break;
          case 'N':
            functionName = "operator<=";
            break;
          case 'O':
            functionName = "operator>";
            break;
          case 'P':
            functionName = "operator>=";
            break;
          case 'Q':
            functionName = "operator,";
            break;
          case 'R':
            functionName = "operator()";
            break;
          case 'S':
            functionName = "operator~";
            break;
          case 'T':
            functionName = "operator^";
            break;
          case 'U':
            functionName = "operator|";
            break;
          case 'V':
            functionName = "operator&&";
            break;
          case 'W':
            functionName = "operator||";
            break;
          case 'X':
            functionName = "operator*=";
            break;
          case 'Y':
            functionName = "operator+=";
            break;
          case 'Z':
            functionName = "operator-=";
            break;
          case '_':
            char next2 = parser.Next();
            switch (next2) {
              case '0':
                functionName = "operator/=";
                break;
              case '1':
                functionName = "operator%=";
                break;
              case '2':
                functionName = "operator>>=";
                break;
              case '3':
                functionName = "operator<<=";
                break;
              case '4':
                functionName = "operator&=";
                break;
              case '5':
                functionName = "operator|=";
                break;
              case '6':
                functionName = "operator^=";
                break;
              case '7':
                functionName = "`vftable'";
                break;
              case '8':
                functionName = "`vbtable'";
                break;
              case '9':
                functionName = "`vcall'";
                break;
              case 'A':
                functionName = "`typeof'";
                break;
              case 'B':
                functionName = "`local static guard'";
                break;
              case 'C':
                functionName = "`string'";
                doAfter = 4;
                break;
              case 'D':
                functionName = "`vbase destructor'";
                break;
              case 'E':
                functionName = "`vector deleting destructor'";
                break;
              case 'F':
                functionName = "`default constructor closure'";
                break;
              case 'G':
                functionName = "`scalar deleting destructor'";
                break;
              case 'H':
                functionName = "`vector constructor iterator'";
                break;
              case 'I':
                functionName = "`vector destructor iterator'";
                break;
              case 'J':
                functionName = "`vector vbase constructor iterator'";
                break;
              case 'K':
                functionName = "`virtual displacement map'";
                break;
              case 'L':
                functionName = "`eh vector constructor iterator'";
                break;
              case 'M':
                functionName = "`eh vector destructor iterator'";
                break;
              case 'N':
                functionName = "`eh vector vbase constructor iterator'";
                break;
              case 'O':
                functionName = "`copy constructor closure'";
                break;
              case 'R':
                parser.Flags |= Flags.NoFunctionReturns;
                switch (parser.Next()) {
                  case '0': {
                    StringArray pmt = new();

                    parser.Next();
                    DemangleDataType(parser, out string? left, out string? right, pmt);
                    if (!DemangleDataType(parser, out left, out right))
                      return false;

                    functionName = left + right + " `RTTI Type Descriptor'";
                    parser.Advance(-1);
                    break;
                  }
                  case '1': {
                    parser.Next();
                    string? n1 = GetNumber(parser);
                    string? n2 = GetNumber(parser);
                    string? n3 = GetNumber(parser);
                    string? n4 = GetNumber(parser);
                    parser.Advance(-1);
                    functionName = "`RTTI Base Class Descriptor at (" + n1 + "," + n2 + "," + n3 + "," + n4 + ")'";
                    break;
                  }
                  case '2':
                    functionName = "`RTTI Base Class Array'";
                    break;
                  case '3':
                    functionName = "`RTTI Class Hierarchy Descriptor'";
                    break;
                  case '4':
                    functionName = "`RTTI Complete Object Locator'";
                    break;
                }

                break;
              case 'S':
                functionName = "`local vftable'";
                break;
              case 'T':
                functionName = "`local vftable constructor closure'";
                break;
              case 'U':
                functionName = "operator new[]";
                break;
              case 'V':
                functionName = "operator delete[]";
                break;
              case 'X':
                functionName = "`placement delete closure'";
                break;
              case 'Y':
                functionName = "`placement delete[] closure'";
                break;
              default:
                return false;
            }

            break;
          default:
            // FIXME: Other operators
            return false;
        }

        parser.Next();
        switch (doAfter) {
          case 1:
          case 2:
            parser.Stack.Push("--null--");
            break;
          case 4:
            undecoratedName = functionName!;
            return true;
          case 6: {
            StringArray pmt = new();
            string? args = GetArgs(parser, pmt, false, '<', '>');
            if (args is not null)
              functionName += args;
            parser.Names.Num = 0;
          }
            parser.Stack.Push(functionName!);
            break;
          default:
            parser.Stack.Push(functionName!);
            break;
        }

        break;
      }
      case '$':
        // Strange construct, it's a name with a template argument list and that's all.
        parser.Next();
        undecoratedName = GetTemplateName(parser);
        return undecoratedName is not null;
      case '?' when parser.Peek(1) == '$':
        doAfter = 5;
        break;
    }

    // Either a class name, or '@' if the symbol is not a class member
    switch (parser.Current) {
      case '@':
        parser.Next();
        break;
      case '$':
        break;
      default:
        // Class the function is associated with, terminated by '@@'
        if (!GetClass(parser))
          return false;

        break;
    }

    switch (doAfter) {
      case 1 or 2:
        // it's time to set the member name for ctor & dtor
        if (parser.Stack.Num <= 1)
          return false;

        if (doAfter == 1)
          parser.Stack.Strings![0] = parser.Stack.Strings[1];
        else
          parser.Stack.Strings![0] = "~" + parser.Stack.Strings[1];
        // ctors and dtors don't have return type
        parser.Flags |= Flags.NoFunctionReturns;
        break;
      case 3:
        parser.Flags &= ~Flags.NoFunctionReturns;
        break;
      case 5:
        parser.Names.Start++;
        break;
    }

    return parser.Current switch {
      // Function/Data type and access level
      >= '0' and <= '9' => HandleData(parser, out undecoratedName),
      >= 'A' and <= 'Z' or '$' => HandleMethod(parser, out undecoratedName, doAfter == 3),
      _ => false
    };
  }

  /// <summary>
  /// Does the final parsing and handling for a variable or a field in a class.
  /// </summary>
  private static bool HandleData(Parser parser, out string? undecoratedName) {
    string? access = null;
    string? memberType = null;

    undecoratedName = null;

    // 0 private static
    // 1 protected static
    // 2 public static
    // 3 private non-static
    // 4 protected non-static
    // 5 public non-static
    // 6 ?? static
    // 7 ?? static

    if ((parser.Flags & Flags.NoAccessSpecifiers) == Flags.Complete) {
      // we only print the access for static members
      access = parser.Current switch {
        '0' => "private ",
        '1' => "protected ",
        '2' => "public ",
        _ => access
      };
    }

    if ((parser.Flags & Flags.NoMemberType) == Flags.Complete) {
      if (parser.Current is >= '0' and <= '2')
        memberType = "static ";
    }

    string? name = GetClassString(parser, 0), left, right, modifier;
    char c = parser.Current;

    parser.Next();
    switch (c) {
      case '0':
      case '1':
      case '2':
      case '3':
      case '4':
      case '5': {
        int mark = parser.Stack.Num;
        StringArray pmt = new();

        if (!DemangleDataType(parser, out left, out right, pmt))
          return false;
        if (!GetModifier(parser, out modifier, out string? ptrModif))
          return false;

        if (modifier is not null && ptrModif is not null)
          modifier = modifier + " " + ptrModif;
        else if (modifier is null)
          modifier = ptrModif;
        parser.Stack.Num = mark;
      }
        break;
      case '6': // compiler generated static
      case '7': // compiler generated static
      {
        left = right = null;
        if (!GetModifier(parser, out modifier, out string? _))
          return false;

        if (parser.Current != '@') {
          string? cls = GetClassName(parser);

          if (cls is null)
            return false;

          right = "{for `" + cls + "'}";
        }
      }
        break;
      case '8':
      case '9':
        modifier = left = right = null;
        break;
      default:
        return false;
    }

    if ((parser.Flags & Flags.NameOnly) == Flags.NameOnly)
      left = right = modifier = null;

    undecoratedName = access + memberType + left
      + (modifier is not null && left is not null ? " " : null) + modifier
      + (modifier is not null || left is not null ? " " : null) + name + right;
    return true;
  }

  private static bool HandleMethod(Parser parser, out string? undecoratedName, bool castOp) {
    int accessId = -1;
    string? access = null;
    string? memberType = null;
    string? modifier = null;
    bool hasArgs = true, hasRet = true;
    char accmem = parser.Current;

    parser.Next();
    undecoratedName = null;

    // FIXME: why 2 possible letters for each option?
    // 'A' private:
    // 'B' private:
    // 'C' private: static
    // 'D' private: static
    // 'E' private: virtual
    // 'F' private: virtual
    // 'G' private: thunk
    // 'H' private: thunk
    // 'I' protected:
    // 'J' protected:
    // 'K' protected: static
    // 'L' protected: static
    // 'M' protected: virtual
    // 'N' protected: virtual
    // 'O' protected: thunk
    // 'P' protected: thunk
    // 'Q' public:
    // 'R' public:
    // 'S' public: static
    // 'T' public: static
    // 'U' public: virtual
    // 'V' public: virtual
    // 'W' public: thunk
    // 'X' public: thunk
    // 'Y'
    // 'Z'
    // "$0" private: thunk vtordisp
    // "$1" private: thunk vtordisp
    // "$2" protected: thunk vtordisp
    // "$3" protected: thunk vtordisp
    // "$4" public: thunk vtordisp
    // "$5" public: thunk vtordisp
    // "$B" vcall thunk
    // "$R" thunk vtordispex

    if (accmem == '$') {
      if (parser.Current is >= '0' and <= '5')
        accessId = (parser.Current - '0') / 2;
      else if (parser.Current == 'R')
        accessId = (parser.Peek(1) - '0') / 2;
      else if (parser.Current != 'B')
        return false;
    }
    else if (accmem is >= 'A' and <= 'Z')
      accessId = (accmem - 'A') / 8;
    else
      return false;

    access = accessId switch {
      0 => "private ",
      1 => "protected ",
      2 => "public ",
      _ => access
    };

    if (accmem == '$' || (accmem - 'A') % 8 == 6 || (accmem - 'A') % 8 == 7)
      access = "[thunk]:" + (access ?? " ");

    memberType = accmem switch {
      '$' when parser.Current != 'B' => "virtual ",
      <= 'X' => ((accmem - 'A') % 8) switch {
        2 or 3 => "static ",
        4 or 5 or 6 or 7 => "virtual ",
        _ => memberType
      },
      _ => memberType
    };

    if ((parser.Flags & Flags.NoAccessSpecifiers) == Flags.NoAccessSpecifiers)
      access = null;
    if ((parser.Flags & Flags.NoMemberType) == Flags.NoMemberType)
      memberType = null;

    // For C#, force ignore of "virtual ", keep "static "
    if (memberType == "virtual ") {
      memberType = null;
    }

    string name = GetClassString(parser, 0);

    switch (accmem) {
      // vcall thunk
      case '$' when parser.Current == 'B': {
        parser.Next();
        string? n = GetNumber(parser);

        if (n is null || parser.Current != 'A') {
          parser.Next();
          return false;
        }

        parser.Next();
        name = name + "{" + n + ",{flat}}' }'";
        hasArgs = false;
        hasRet = false;
        break;
      }
      // vtordispex thunk
      case '$' when parser.Current == 'R': {
        parser.Advance(2);
        string? n1 = GetNumber(parser);
        string? n2 = GetNumber(parser);
        string? n3 = GetNumber(parser);
        string? n4 = GetNumber(parser);

        if (n1 is null || n2 is null || n3 is null || n4 is null)
          return false;

        name = name + "`vtordispex{" + n1 + "," + n2 + "," + n3 + "," + n4 + "}' ";
        break;
      }
      // vtordisp thunk
      case '$': {
        parser.Next();
        string? n1 = GetNumber(parser);
        string? n2 = GetNumber(parser);

        if (n1 is null || n2 is null)
          return false;

        name = name + "`vtordisp{" + n1 + "," + n2 + "}' ";
        break;
      }
      default: {
        if ((accmem - 'A') % 8 == 6 || (accmem - 'A') % 8 == 7) // a thunk
          name = name + "`adjustor{" + GetNumber(parser) + "}' ";
        break;
      }
    }

    if (hasArgs && (accmem == '$' || (accmem <= 'X' && (accmem - 'A') % 8 != 2 && (accmem - 'A') % 8 != 3))) {
      // Implicit 'this' pointer
      // If there is an implicit this pointer, const modifier follows
      if (!GetModifier(parser, out modifier, out string? ptrModif))
        return false;

      if (modifier is not null || ptrModif is not null)
        modifier = modifier + " " + ptrModif;
    }

    if (!GetCallingConvention(parser.Current, out string? callConv, out string? exported, parser.Flags)) {
      parser.Next();
      return false;
    }

    parser.Next();

    StringArray pmt = new();
    string? retLeft = null, retRight = null;

    // Return type, or @ if 'void'
    if (hasRet) {
      if (parser.Current == '@') {
        retLeft = "void";
        retRight = null;
        parser.Next();
      }
      else if (!DemangleDataType(parser, out retLeft, out retRight, pmt)) {
        return false;
      }
    }

    if (!hasRet || (parser.Flags & Flags.NoFunctionReturns) == Flags.NoFunctionReturns)
      retLeft = retRight = null;
    if (castOp) {
      name = name + retLeft + retRight;
      retLeft = retRight = null;
    }

    int mark = parser.Stack.Num;
    string? argsStr = null;
    if (hasArgs) {
      argsStr = GetArgs(parser, pmt, true, '(', ')');
      if (argsStr is null)
        return false;
    }

    if ((parser.Flags & Flags.NameOnly) == Flags.NameOnly)
      argsStr = modifier = null;
    if ((parser.Flags & Flags.NoThisType) == Flags.NoThisType)
      modifier = null;
    parser.Stack.Num = mark;

    // Note: '()' after 'Z' means 'throws', but we don't care here
    // Yet!!! FIXME
    undecoratedName = access + memberType + retLeft
      + (retLeft is not null && retRight is null ? " " : null) + callConv
      + (callConv is not null ? " " : null) + exported + name + argsStr + modifier + retRight;
    return true;
  }

  private static bool DemangleDataType(Parser parser, [NotNullWhen(true)] out string? left, [NotNullWhen(true)] out string? right, StringArray? pmt = null,
    bool inArgs = false) {
    bool addPmt = true;
    char dt = parser.Current;

    parser.Next();
    left = right = null;
    switch (dt) {
      case '_':
        left = GetExtendedType(parser.Current);
        parser.Next();
        break;
      case 'C':
      case 'D':
      case 'E':
      case 'F':
      case 'G':
      case 'H':
      case 'I':
      case 'J':
      case 'K':
      case 'M':
      case 'N':
      case 'O':
      case 'X':
      case 'Z':
        left = GetSimpleType(dt);
        addPmt = false;
        break;
      case 'T': // union
      case 'U': // struct
      case 'V': // class
      case 'Y': // cointerface
      {
        string? typeName = GetClassName(parser);

        if (typeName is null)
          return false;

        if ((parser.Flags & Flags.NoComplexType) != Flags.NoComplexType) {
          typeName = dt switch {
            'T' => "union " + typeName,
            'U' => "struct " + typeName,
            'V' => "class " + typeName,
            'Y' => "cointerface " + typeName,
            _ => typeName
          };
        }

        left = typeName;
      }
        break;
      case '?':
        // not all the time is seems
        if (inArgs) {
          string? ptr = GetNumber(parser);

          if (ptr is null)
            return false;

          left = "`template-parameter-" + ptr + "'";
        }
        else {
          if (!GetModifiedType(parser, out left, out right, pmt, '?', inArgs))
            return false;
        }

        break;
      case 'A': // reference
      case 'B': // volatile reference
        if (!GetModifiedType(parser, out left, out right, pmt, dt, inArgs))
          return false;

        break;
      case 'Q': // const pointer
      case 'R': // volatile pointer
      case 'S': // const volatile pointer
        if (!GetModifiedType(parser, out left, out right, pmt, inArgs ? dt : 'P', inArgs))
          return false;

        break;
      case 'P':
        if (char.IsDigit(parser.Current)) {
          // FIXME:
          //  P6 = Function pointer
          //  P8 = Member function pointer
          //  others who knows..
          if (parser.Current == '8') {
            int mark = parser.Stack.Num;
            parser.Next();
            string? cls = GetClassName(parser);
            if (cls is null)
              return false;
            if (!GetModifier(parser, out string? modifier, out string? ptrModif))
              return false;

            if (modifier is not null)
              modifier += " " + ptrModif;
            else if (ptrModif is not null)
              modifier = " " + ptrModif;
            if (!GetCallingConvention(parser.Current, out string? callConv, out string? _,
                  parser.Flags & ~Flags.NoAllocationLanguage)) {
              parser.Next();
              return false;
            }

            parser.Next();
            if (!DemangleDataType(parser, out string? subLeft, out string? subRight, pmt))
              return false;

            string? args = GetArgs(parser, pmt, true, '(', ')');
            if (args is null)
              return false;

            parser.Stack.Num = mark;
            left = subLeft + subRight + " (" + callConv + " " + cls + ".*";
            right = ")" + args + modifier;
          }
          else if (parser.Current == '6') {
            int mark = parser.Stack.Num;
            parser.Next();
            if (!GetCallingConvention(parser.Current, out string? callConv, out string? _,
                  parser.Flags & ~Flags.NoAllocationLanguage)) {
              parser.Next();
              return false;
            }

            parser.Next();
            if (!DemangleDataType(parser, out string? subLeft, out string? subRight, pmt))
              return false;

            string? args = GetArgs(parser, pmt, true, '(', ')');
            if (args is null)
              return false;

            parser.Stack.Num = mark;
            left = subLeft + subRight + " (" + callConv + "*";
            right = ")" + args;
          }
          else
            return false;
        }
        else if (!GetModifiedType(parser, out left, out right, pmt, 'P', inArgs))
          return false;

        break;
      case 'W':
        if (parser.Current == '4') {
          parser.Next();
          string? enumName = GetClassName(parser);
          if (enumName is null)
            return false;

          if ((parser.Flags & Flags.NoComplexType) == Flags.NoComplexType)
            left = enumName;
          else
            left = "enum " + enumName;
        }
        else
          return false;

        break;
      case '0':
      case '1':
      case '2':
      case '3':
      case '4':
      case '5':
      case '6':
      case '7':
      case '8':
      case '9':
        // Referring back to previously parsed type
        // left and right are pushed as two separate strings
        if (pmt is null)
          return false;

        left = pmt.Get((dt - '0') * 2);
        right = pmt.Get((dt - '0') * 2 + 1);
        if (left is null)
          return false;

        addPmt = false;
        break;
      case '$': {
        char sp = parser.Current;
        parser.Next();
        switch (sp) {
          case '0': {
            left = GetNumber(parser);
            if (left is null)
              return false;

            break;
          }
          case 'D': {
            if (GetNumber(parser) is not {} p1) {
              return false;
            }

            left = "`tepmlate-parameter" + p1 + "'";
            break;
          }
          case 'F': {
            if (GetNumber(parser) is not {} p1 ||
                GetNumber(parser) is not {} p2) {
              return false;
            }

            left = "{" + p1 + "," + p2 + "}";
          }
            break;
          case 'G': {
            if (GetNumber(parser) is not {} p1 ||
                GetNumber(parser) is not {} p2 ||
                GetNumber(parser) is not {} p3) {
              return false;
            }

            left = "{" + p1 + "," + p2 + "," + p3 + "}";
            break;
          }
          case 'Q': {
            left = GetNumber(parser);
            if (left is null)
              return false;

            left = "`non-type-template-parameter" + left + "'";
            break;
          }
          case '$':
            switch (parser.Current) {
              case 'B': {
                int mark = parser.Stack.Num;
                string? arr = null;
                parser.Next();

                // multidimensional arrays
                if (parser.Current == 'Y') {
                  parser.Next();
                  string? n1 = GetNumber(parser);
                  if (n1 is null || !int.TryParse(n1, out int num))
                    return false;

                  while (num-- > 0)
                    arr += "[" + GetNumber(parser) + "]";
                }

                if (!DemangleDataType(parser, out string? subLeft, out string? subRight, pmt))
                  return false;

                if (arr is not null)
                  left = subLeft + " " + arr;
                else
                  left = subLeft;
                right = subRight;
                parser.Stack.Num = mark;
                break;
              }
              case 'C': {
                parser.Next();
                if (!GetModifier(parser, out string? ptr, out string? _))
                  return false;
                if (!DemangleDataType(parser, out left, out right, pmt, inArgs))
                  return false;

                left = left + " " + ptr;
                break;
              }
            }

            break;
        }
      }
        break;
      default:
        return false;
    }

    if (addPmt && pmt is not null && inArgs) {
      // left and right are pushed as two separate strings
      pmt.Push(left ?? "");
      pmt.Push(right ?? "");
    }

    return left is not null;
  }

  private static bool GetCallingConvention(char ch, out string? callConv, out string? exported, Flags flags) {
    callConv = exported = null;
    if ((flags & (Flags.NoMicrosoftKeywords | Flags.NoAllocationLanguage)) != Flags.Complete) {
      return true;
    }

    bool ret;
    bool noLeadingUnderscores = (flags & Flags.NoLeadingUnderscores) == Flags.NoLeadingUnderscores;
    if ((ch - 'A') % 2 == 1) {
      exported = noLeadingUnderscores ? "dll_export " : "__dll_export ";
    }

    if (noLeadingUnderscores) {
      (ret, callConv) = ch switch {
        'A' or 'B' => (true, "cdecl"),
        'C' or 'D' => (true, "pascal"),
        'E' or 'F' => (true, "thiscall"),
        'G' or 'H' => (true, "stdcall"),
        'I' or 'J' => (true, "fastcall"),
        'K' or 'L' => (true, null),
        'M' => (true, "clrcall"),
        _ => (false, null)
      };
    }
    else {
      (ret, callConv) = ch switch {
        'A' or 'B' => (true, "__cdecl"),
        'C' or 'D' => (true, "__pascal"),
        'E' or 'F' => (true, "__thiscall"),
        'G' or 'H' => (true, "__stdcall"),
        'I' or 'J' => (true, "__fastcall"),
        'K' or 'L' => (true, null),
        'M' => (true, "__clrcall"),
        _ => (false, null)
      };
    }


    return ret;
  }

  private static bool GetModifiedType(Parser parser, out string? left, out string? right, StringArray? pmt, char modif,
    bool inArgs) {
    string? ptrModif = "";

    if (parser.Current == 'E') {
      if ((parser.Flags & Flags.NoMicrosoftKeywords) != Flags.NoMicrosoftKeywords) {
        ptrModif = (parser.Flags & Flags.NoLeadingUnderscores) == Flags.NoLeadingUnderscores ? " ptr64" : " __ptr64";
      }

      parser.Next();
    }

    left = right = null;
    string? strModif = null;
    // strModif = modif switch {
    //   'A' => " &" + ptrModif,
    //   'B' => " &" + ptrModif + " volatile",
    //   'P' => " *" + ptrModif,
    //   'Q' => " *" + ptrModif + " const",
    //   'R' => " *" + ptrModif + " volatile",
    //   'S' => " *" + ptrModif + " const volatile",
    //   '?' => "",
    //   _ => null
    // };

    if (modif is 'A' or 'B' or 'P' or 'Q' or 'R' or 'S') {
      strModif = '*' + ptrModif;
    }


    if (strModif is null) {
      return false;
    }

    if (!GetModifier(parser, out string? modifier, out ptrModif)) {
      return true;
    }

    int mark = parser.Stack.Num;

    /* multidimensional arrays */
    if (parser.Current == 'Y') {
      parser.Next();
      string? n1 = GetNumber(parser);
      if (n1 is null || !int.TryParse(n1, out int num))
        return false;

      if (strModif is { Length: > 0 } && strModif[0] == ' ' && modifier is null)
        strModif = strModif[1..];

      if (modifier is not null) {
        strModif = " (" + modifier + strModif + ")";
        modifier = null;
      }
      else
        strModif = " (" + strModif + ")";

      while (num-- > 0)
        strModif += "[" + GetNumber(parser) + "]";
    }

    // Recurse to get the referred-to type
    if (!DemangleDataType(parser, out string? subLeft, out string? subRight, pmt))
      return false;

    if (modifier is not null)
      left = subLeft + " " + modifier + strModif;
    else {
      // don't insert a space between duplicate '*'
      if (!inArgs && strModif is [_, '*', ..] && subLeft[^1] == '*')
        strModif = strModif[1..];
      left = subLeft + strModif;
    }

    right = subRight;
    parser.Stack.Num = mark;

    return true;
  }

  private static bool GetModifier(Parser parser, out string? ret, out string? ptrModif) {
    ptrModif = null;
    if (parser.Current == 'E') {
      if ((parser.Flags & Flags.NoMicrosoftKeywords) != Flags.NoMicrosoftKeywords) {
        ptrModif = (parser.Flags & Flags.NoLeadingUnderscores) == Flags.NoLeadingUnderscores ? "ptr64" : "__ptr64";
      }

      parser.Next();
    }

    (bool result, ret) = parser.Current switch {
      'A' => (true, null),
      'B' => (true, "const"),
      'C' => (true, "volatile"),
      'D' => (true, "const volatile"),
      _ => (false, null)
    };

    parser.Next();
    // In C# we don't care about these modifiers
    ret = null;
    return result;
  }

  private static string? GetClassName(Parser parser) {
    int mark = parser.Stack.Num;
    string? result = null;

    if (GetClass(parser))
      result = GetClassString(parser, mark);
    parser.Stack.Num = mark;
    return result;
  }

  private static string GetClassString(Parser parser, int start) {
    StringBuilder sb = new();

    for (int i = parser.Stack.Num - 1; i >= start; i--) {
      sb.Append(parser.Stack.Strings![i]);
      if (i > start)
        sb.Append(".");
    }

    return sb.ToString();
  }

  /// <summary>
  /// Parses class as a list of parent-classes, terminated by '@' and stores the
  /// result in 'a' array.Each parent-classes, as well as the inner element
  /// (either field/method name or class name), are represented in the mangled
  /// name by a literal name([a-zA-Z0-9_]+ terminated by '@') or a back reference
  /// ([0 - 9]) or a name with template arguments('?$' literal name followed by the
  /// template argument list). The class name components appear in the reverse
  /// order in the mangled name, e.g aaa@bbb @ccc@@ will be demangled to
  /// <code>ccc::bbb::aaa</code>
  /// For each of these class name components a string will be allocated in the
  /// array.
  /// </summary>
  private static bool GetClass(Parser parser) {
    string? name = null;

    while (parser.Current != '@') {
      switch (parser.Current) {
        case '\0':
          return false;

        case '0':
        case '1':
        case '2':
        case '3':
        case '4':
        case '5':
        case '6':
        case '7':
        case '8':
        case '9':
          name = parser.Names.Get(parser.Current - '0');
          parser.Next();
          break;
        case '?':
          switch (parser.Next()) {
            case '$':
              parser.Next();
              name = GetTemplateName(parser);
              if (name is not null)
                parser.Names.Push(name);
              break;
            case '?': {
              StringArray stack = parser.Stack;
              int start = parser.Names.Start;
              int num = parser.Names.Num;

              parser.Stack = new StringArray();
              if (SymbolDemangle(parser, out string? undecoratedName)) {
                name = $"`{undecoratedName}'";
              }
              parser.Names.Start = start;
              parser.Names.Num = num;
              parser.Stack = stack;
            }
              break;
            default:
              name = GetNumber(parser);
              if (name is null)
                return false;

              name = $"`{name}'";
              break;
          }

          break;
        default:
          name = GetLiteralString(parser);
          break;
      }

      if (name is not null)
        parser.Stack.Push(name);
    }

    parser.Next();
    return true;
  }

  private static string? GetNumber(Parser parser) {
    bool sign = false;
    int number = 0;

    if (parser.Current == '?') {
      sign = true;
      parser.Next();
    }

    switch (parser.Current) {
      case >= '0' and <= '9':
        number = parser.Current - '0' + 1;
        break;
      case >= 'A' and <= 'P': {
        while (parser.Current is >= 'A' and <= 'P') {
          number *= 16;
          number += parser.Current - 'A';
          parser.Next();
        }

        if (parser.Current != '@')
          return null;

        break;
      }
      default:
        return null;
    }

    parser.Next();

    string s = ((uint)number).ToString();
    if (sign)
      s = "-" + s;
    return s;
  }

  /// <summary>
  /// Parses a name with a template argument list and returns it as
  /// a string.
  /// In a template argument list the back reference to the names
  /// table is separately created. '0' points to the class component
  /// name with the template arguments.  We use the same stack array
  /// to hold the names but save/restore the stack state before/after
  /// parsing the template argument list.
  /// </summary>
  private static string? GetTemplateName(Parser parser) {
    int numMark = parser.Names.Num;
    int startMark = parser.Names.Start;
    int stackMark = parser.Stack.Num;

    parser.Names.Start = parser.Names.Num;
    string? name = GetLiteralString(parser);
    if (name is null) {
      parser.Names.Start = startMark;
      return null;
    }

    StringArray arrayPmt = new();
    string? args = GetArgs(parser, arrayPmt, false, '<', '>');
    if (args is not null)
      name += args;
    parser.Names.Num = numMark;
    parser.Names.Start = startMark;
    parser.Stack.Num = stackMark;
    return name;
  }

  private static string? GetArgs(Parser parser, StringArray? pmt, bool zTerm, char openChar, char closeChar) {
    List<string> args = [];

    // Now come the function arguments
    while (parser.Current != '\0') {
      // Decode each data type and append it to the argument list
      if (parser.Current == '@') {
        parser.Next();
        break;
      }

      if (!DemangleDataType(parser, out string? left, out string? right, pmt, true))
        return null;

      // 'void' terminates an argument list in a function
      if (zTerm && left == "void")
        break;

      args.Add(left + right);
      if (left == "...")
        break;
    }

    // Functions are always terminated by 'Z'. If we made it this far and
    // don't find it, we have incorrectly identified a data type.
    if (zTerm) {
      if (parser.Current != 'Z') {
        parser.Next();
        return null;
      }

      parser.Next();
    }

    if (args.Count == 0 || args is ["void"])
      return openChar + "void" + closeChar;

    string argsJoined = string.Join(",", args);
    if (closeChar == '>' && argsJoined[^1] == '>')
      return openChar + argsJoined + " >";

    return openChar + argsJoined + closeChar;
  }

  /// <summary>
  /// Gets the literal name from the current position in the mangled
  /// symbol to the first '@' character. It pushes the parsed name to
  /// the symbol names stack and returns a pointer to it or <c>null</c> in
  /// case of an error.
  /// </summary>
  private static string? GetLiteralString(Parser parser) {
    int startIndex = parser.Index;

    do {
      if (parser.Current is not (>= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '$' or '<' or '>'
          or '-'))
        return null;
    } while (parser.Next() != '@');

    string literal = parser.Input.Substring(startIndex, parser.Index - startIndex);
    parser.Names.Push(literal);
    parser.Next();
    return literal;
  }

  private static string? GetSimpleType(char c) => c switch {
    'C' => nameof(CppSignedChar), // signed char
    'D' => nameof(CppChar), // char
    'E' => nameof(CppUnsignedChar), // unsigned char
    'F' => nameof(CppInt16Short), // short
    'G' => nameof(CppUInt16Short), // unsigned short
    'H' => "short", // int
    'I' => "ushort", // unsigned int
    'J' => "int", // long
    'K' => "uint", // unsigned long
    'M' => "float", // float
    'N' => "double", // double
    'O' => "double", // long double
    'X' => "void",
    'Z' => "__arglist", // "..."
    _ => null
  };

  private static string? GetExtendedType(char c) => c switch {
    'D' => nameof(CppInt8), // __int8,
    'E' => nameof(CppUInt8), // unsigned __int8,
    'F' => nameof(CppInt16), // __int16,
    'G' => nameof(CppUInt16), // unsigned __int16,
    'H' => nameof(CppInt32), // __int32,
    'I' => nameof(CppUInt32), // unsigned __int32,
    'J' => nameof(CppInt64), // __int64,
    'K' => nameof(CppUInt64), // unsigned __int64,
    'L' => nameof(CppInt128), // __int128,
    'M' => nameof(CppUInt128), // unsigned __int128,
    'N' => "bool", // bool
    'W' => "char", // wchar_t
    _ => null
  };
}
