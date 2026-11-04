using System;

namespace Leptonica;

/*
 *  dnahash.c: Hash table of L_Dna.
 *
 *      Dnahash creation
 *          L_DnaHash.Create (l_dnaHashCreate)
 *
 *      Dnahash accessor and modifier
 *          L_DnaHash.GetDna (l_dnaHashGetDna), Add (l_dnaHashAdd)
 *
 *  Not ported: l_dnaHashDestroy (memory management only).
 *
 *  (1) The DnaHash is an array of Dna.  It is a simple method used for
 *      fast lookup of templates in the jbig2 classifier (jbclass.c).
 */

/// <summary>Array of L_Dna, indexed by a hashed key (struct L_DnaHash).</summary>
public sealed class L_DnaHash
{
    private readonly int nbuckets;
    private readonly int initsize;     /* initial size of each dna that is made */
    private readonly L_Dna?[] dna;     /* array of L_Dna */

    private L_DnaHash(int nbuckets, int initsize)
    {
        this.nbuckets = nbuckets;
        this.initsize = initsize;
        dna = new L_Dna?[nbuckets];
    }

    /// <summary>The number of buckets in the hash table.</summary>
    public int NBuckets => nbuckets;

    /*--------------------------------------------------------------------------*
     *                         Dnahash creation                                 *
     *--------------------------------------------------------------------------*/
    // leptonica: l_dnaHashCreate (dnahash.c)
    /// <summary>Creates a hash table of L_Dna.</summary>
    /// <param name="nbuckets">the number of buckets in the hash table,
    ///     which should be prime.</param>
    /// <param name="initsize">initial size of each allocated dna; 0 for default</param>
    /// <remarks>
    /// (1) If %nbuckets is not prime, use the next largest prime.
    /// (2) In use, stored dna are created by l_dnaHashAdd().
    /// </remarks>
    public static L_DnaHash Create(int nbuckets, int initsize)
    {
        if (nbuckets <= 0)
            throw Lept.Arg("l_dnaHashCreate", "negative hash size");
        Utils.LeptIsPrime((ulong)nbuckets, out bool is_prime, out _);
        if (!is_prime) {
            uint newsize = Utils.FindNextLargerPrime(nbuckets);
            nbuckets = (int)newsize;
        }

        return new L_DnaHash(nbuckets, initsize);
    }

    /*--------------------------------------------------------------------------*
     *                      Dnahash accessor and modifier                       *
     *--------------------------------------------------------------------------*/
    // leptonica: l_dnaHashGetDna (dnahash.c)
    /// <summary>Gets the dna in the bucket of the key.</summary>
    /// <param name="key">key to be hashed into a bucket number</param>
    /// <param name="copyflag">L_NOCOPY, L_COPY, L_CLONE</param>
    /// <returns>the dna, or null if the bucket is empty</returns>
    public L_Dna? GetDna(ulong key, int copyflag)
    {
        int bucket = (int)(key % (ulong)nbuckets);
        L_Dna? da = dna[bucket];
        if (da != null) {
            if (copyflag == L.L_NOCOPY)
                return da;
            else if (copyflag == L.L_COPY)
                return da.Copy();
            else
                return da.Clone();
        }
        else
            return null;
    }

    // leptonica: l_dnaHashAdd (dnahash.c)
    /// <summary>Appends a value to the dna in the bucket of the key.</summary>
    /// <param name="key">key to be hashed into a bucket number</param>
    /// <param name="value">float value to be appended to the specific dna</param>
    public void Add(ulong key, double value)
    {
        int bucket = (int)(key % (ulong)nbuckets);
        L_Dna? da = dna[bucket];
        if (da == null) {
            da = L_Dna.Create(initsize);
            dna[bucket] = da;
        }
        da.AddNumber(value);
    }
}
