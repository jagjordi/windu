namespace Windu;

/// <summary>Minimal shell-style glob matcher supporting * ? and [...] character classes.</summary>
public static class Glob
{
    public static bool IsMatch(string input, string pattern)
        => Match(input, 0, pattern, 0);

    private static bool Match(string s, int si, string p, int pi)
    {
        while (pi < p.Length)
        {
            char pc = p[pi];
            if (pc == '*')
            {
                // Collapse runs of '*' then try every split point.
                while (pi < p.Length && p[pi] == '*') pi++;
                if (pi == p.Length) return true;
                for (int k = si; k <= s.Length; k++)
                    if (Match(s, k, p, pi)) return true;
                return false;
            }

            if (si >= s.Length) return false;

            if (pc == '?')
            {
                si++; pi++;
                continue;
            }

            if (pc == '[')
            {
                int close = p.IndexOf(']', pi + 1);
                if (close < 0) return false;
                var set = p.AsSpan(pi + 1, close - pi - 1);
                bool negate = set.Length > 0 && (set[0] == '!' || set[0] == '^');
                if (negate) set = set[1..];
                bool hit = false;
                for (int i = 0; i < set.Length; i++)
                {
                    if (i + 2 < set.Length && set[i + 1] == '-')
                    {
                        if (char.ToLowerInvariant(s[si]) >= char.ToLowerInvariant(set[i]) &&
                            char.ToLowerInvariant(s[si]) <= char.ToLowerInvariant(set[i + 2]))
                            hit = true;
                        i += 2;
                    }
                    else if (char.ToLowerInvariant(set[i]) == char.ToLowerInvariant(s[si]))
                        hit = true;
                }
                if (hit == negate) return false;
                si++; pi = close + 1;
                continue;
            }

            if (char.ToLowerInvariant(pc) != char.ToLowerInvariant(s[si])) return false;
            si++; pi++;
        }
        return si == s.Length;
    }
}
