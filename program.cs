using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

class Program
{
    static BigInteger ArcCotParallel(BigInteger scale, int x, int threads)
    {
        if (threads < 1)
            throw new ArgumentOutOfRangeException(nameof(threads));

        BigInteger baseTerm = scale / x;
        BigInteger xSq = x * x;
        const int chunkSize = 4096; // 512

        long nextChunkIndex = 0;
        long totalSumLock = 0; // nur als Spin/Interlocked-Guard für BigInteger-Addition
        BigInteger totalSum = BigInteger.Zero;
        int finished = 0;

        void Worker()
        {
            while (Volatile.Read(ref finished) == 0)
            {
                long chunk = Interlocked.Increment(ref nextChunkIndex) - 1;
                long startK = chunk * chunkSize;
                long endK = startK + chunkSize;

                BigInteger xPow = (startK == 0)
                    ? baseTerm
                    : baseTerm / BigInteger.Pow(xSq, (int)startK);

                BigInteger localSum = BigInteger.Zero;
                bool anyNonZero = false;

                for (long k = startK; k < endK; k++)
                {
                    if (k > startK)
                        xPow /= xSq;

                    BigInteger term = xPow / (2 * k + 1);
                    if (term.IsZero)
                        break;

                    anyNonZero = true;
                    if ((k & 1) == 0)
                        localSum += term;
                    else
                        localSum -= term;
                }

                if (!localSum.IsZero)
                {
                    while (Interlocked.CompareExchange(ref totalSumLock, 1, 0) != 0)
                        Thread.SpinWait(10);

                    totalSum += localSum;
                    Volatile.Write(ref totalSumLock, 0);
                }

                if (!anyNonZero)
                {
                    Interlocked.Exchange(ref finished, 1);
                    break;
                }
            }
        }

        Task[] workers = new Task[threads];
        for (int i = 0; i < threads; i++)
            workers[i] = Task.Run(Worker);

        Task.WaitAll(workers);
        return totalSum;
    }

    static string CalcPiDigits(int digits, int threads = 8)
    {
        if (digits < 0)
            throw new ArgumentOutOfRangeException(nameof(digits));

        int guard = (int)Math.Ceiling(Math.Log10(Math.Max(10, digits))) + 6;
        BigInteger scale = BigInteger.Pow(10, digits + guard);

        // Threads auf ArcCot(5) und ArcCot(239) aufteilen (mind. je 1 Thread)
        int threads5 = Math.Max(1, threads / 2);
        int threads239 = Math.Max(1, threads - threads5);

        Task<BigInteger> atan5Task = Task.Run(() => ArcCotParallel(scale, 5, threads5));
        Task<BigInteger> atan239Task = Task.Run(() => ArcCotParallel(scale, 239, threads239));
        Task<BigInteger> guardPowTask = Task.Run(() => BigInteger.Pow(10, guard));

        Task.WaitAll(atan5Task, atan239Task, guardPowTask);

        BigInteger piScaled = 4 * (4 * atan5Task.Result - atan239Task.Result);
        BigInteger piExact = piScaled / guardPowTask.Result;

        string s = piExact.ToString();
        if (digits == 0)
            return s[0].ToString();

        return $"{s[0]}.{s[1..]}";
    }

    static void Main()
    {
        int threads = 4; // z. B. 1, 2, 4, 8 oder Environment.ProcessorCount
        Console.WriteLine(CalcPiDigits(100000, threads));
    }
}
