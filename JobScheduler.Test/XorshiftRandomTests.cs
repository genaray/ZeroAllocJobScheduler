using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JobScheduler.Test;

[TestFixture]
internal class XorshiftRandomTests
{
    [Test]
    [TestCase(16, 64, 1000)]
    [TestCase(0, 16, 100)]
    [TestCase(0, 8, 100)]
    [TestCase(0, 4, 100)]
    [TestCase(0, 2, 100)]
    public void XorshiftHasRandomDistribution(int start, int end, int count)
    {
        var random = new XorshiftRandom();

        var randoms = Enumerable.Range(0, count)
            .Select(i => random.Next(start, end))
            .ToList();

        var randoms2 = Enumerable.Range(0, count)
            .Select(i => random.Next(start, end))
            .ToList();

        Assert.Multiple(() =>
        {

            // Ensure that we've generated all the possible elements (assumes count >> end - start)
            Assert.That(randoms.Distinct().Count, Is.EqualTo(end - start));
            Assert.That(randoms2.Distinct().Count, Is.EqualTo(end - start));
            // Ensure that we've not made equivalent lists
            Assert.That(randoms, Is.Not.EqualTo(randoms2));
        });
    }
}
