using AutoFixture;

namespace Ufo.UnitTests
{
    public class BaseTest
    {
        protected readonly IFixture Fixture;

        public BaseTest()
        {
            Fixture = new Fixture();
            Fixture.Behaviors.Remove(new ThrowingRecursionBehavior());
            Fixture.Behaviors.Add(new OmitOnRecursionBehavior());
        }
    }
}