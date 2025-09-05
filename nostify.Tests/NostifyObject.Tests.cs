using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Xunit;
using nostify;

namespace nostify.Tests;

public class NostifyObjectTests
{
    // Test implementation of NostifyObject for testing purposes
    public class TestNostifyObject : NostifyObject, IAggregate
    {
        public static string aggregateType => "TestAggregate";
        public static string currentStateContainerName => $"{aggregateType}CurrentState";

        public bool isDeleted { get; set; }
        public string? name { get; set; }
        public int age { get; set; }
        public bool isActive { get; set; }
        public DateTime? createdDate { get; set; }
        public List<string>? tags { get; set; }

        public override void Apply(IEvent eventToApply)
        {
            // Implementation not needed for these tests
            throw new NotImplementedException("Apply method not implemented for test");
        }
    }

    // Another test class with different property names
    public class TestProjection : NostifyObject, IProjection
    {
        public static string containerName => "TestProjection";

        public bool initialized { get; set; }
        public string? fullName { get; set; }
        public int userAge { get; set; }
        public string? status { get; set; }

        public override void Apply(IEvent eventToApply)
        {
            throw new NotImplementedException("Apply method not implemented for test");
        }
    }

    [Fact]
    public void DefaultProperties_ShouldHaveCorrectDefaultValues()
    {
        // Arrange & Act
        var obj = new TestNostifyObject();

        // Assert
        Assert.Equal(-1, obj.ttl);
        Assert.Equal(Guid.Empty, obj.tenantId);
        Assert.Equal(Guid.Empty, obj.id);
    }

    [Fact]
    public void Properties_ShouldBeSettableAndGettable()
    {
        // Arrange
        var obj = new TestNostifyObject();
        var testId = Guid.NewGuid();
        var testTenantId = Guid.NewGuid();
        var testTtl = 3600;

        // Act
        obj.id = testId;
        obj.tenantId = testTenantId;
        obj.ttl = testTtl;

        // Assert
        Assert.Equal(testId, obj.id);
        Assert.Equal(testTenantId, obj.tenantId);
        Assert.Equal(testTtl, obj.ttl);
    }

    [Fact]
    public void UpdateProperties_WithMatchingPropertyNames_ShouldUpdateAllProperties()
    {
        // Arrange
        var obj = new TestNostifyObject();
        var payload = new
        {
            name = "John Doe",
            age = 30,
            isActive = true,
            createdDate = new DateTime(2023, 1, 1),
            tags = new List<string> { "tag1", "tag2" }
        };

        // Act
        obj.UpdateProperties<TestNostifyObject>(payload);

        // Assert
        Assert.Equal("John Doe", obj.name);
        Assert.Equal(30, obj.age);
        Assert.True(obj.isActive);
        Assert.Equal(new DateTime(2023, 1, 1), obj.createdDate);
        Assert.NotNull(obj.tags);
        Assert.Equal(2, obj.tags.Count);
        Assert.Contains("tag1", obj.tags);
        Assert.Contains("tag2", obj.tags);
    }

    [Fact]
    public void UpdateProperties_WithPartialPayload_ShouldUpdateOnlyMatchingProperties()
    {
        // Arrange
        var obj = new TestNostifyObject
        {
            name = "Original Name",
            age = 25,
            isActive = false
        };
        var payload = new
        {
            name = "Updated Name",
            age = 35
            // isActive not included in payload
        };

        // Act
        obj.UpdateProperties<TestNostifyObject>(payload);

        // Assert
        Assert.Equal("Updated Name", obj.name);
        Assert.Equal(35, obj.age);
        Assert.False(obj.isActive); // Should remain unchanged
    }

    [Fact]
    public void UpdateProperties_WithExtraPropertiesInPayload_ShouldIgnoreUnmatchedProperties()
    {
        // Arrange
        var obj = new TestNostifyObject();
        var payload = new
        {
            name = "John Doe",
            age = 30,
            unknownProperty = "This should be ignored",
            anotherUnknown = 123
        };

        // Act
        obj.UpdateProperties<TestNostifyObject>(payload);

        // Assert
        Assert.Equal("John Doe", obj.name);
        Assert.Equal(30, obj.age);
        // unknownProperty and anotherUnknown should be ignored without error
    }

    [Fact]
    public void UpdateProperties_WithPropertyPairs_ShouldMapPropertiesCorrectly()
    {
        // Arrange
        var obj = new TestProjection();
        var payload = new
        {
            name = "John Doe",
            age = 30,
            active = "Active"
        };
        var propertyPairs = new Dictionary<string, string>
        {
            { "name", "fullName" },
            { "age", "userAge" },
            { "active", "status" }
        };

        // Act
        obj.UpdateProperties<TestProjection>(payload, propertyPairs, strict: true);

        // Assert
        Assert.Equal("John Doe", obj.fullName);
        Assert.Equal(30, obj.userAge);
        Assert.Equal("Active", obj.status);
    }

    [Fact]
    public void UpdateProperties_WithPropertyPairsNonStrict_ShouldMapPairsAndMatchByName()
    {
        // Arrange
        var obj = new TestProjection();
        var payload = new
        {
            name = "John Doe",
            userAge = 30,  // This matches by name
            active = "Active"
        };
        var propertyPairs = new Dictionary<string, string>
        {
            { "name", "fullName" },
            { "active", "status" }
        };

        // Act
        obj.UpdateProperties<TestProjection>(payload, propertyPairs, strict: false);

        // Assert
        Assert.Equal("John Doe", obj.fullName); // Mapped via propertyPairs
        Assert.Equal(30, obj.userAge); // Matched by name
        Assert.Equal("Active", obj.status); // Mapped via propertyPairs
    }

    [Fact]
    public void UpdateProperties_WithPropertyPairsStrict_ShouldOnlyUpdateMappedProperties()
    {
        // Arrange
        var obj = new TestProjection();
        var payload = new
        {
            name = "John Doe",
            userAge = 30,  // This should be ignored in strict mode
            active = "Active"
        };
        var propertyPairs = new Dictionary<string, string>
        {
            { "name", "fullName" },
            { "active", "status" }
        };

        // Act
        obj.UpdateProperties<TestProjection>(payload, propertyPairs, strict: true);

        // Assert
        Assert.Equal("John Doe", obj.fullName);
        Assert.Equal(0, obj.userAge); // Should remain default value
        Assert.Equal("Active", obj.status);
    }

    [Fact]
    public void UpdateProperty_WithValidPropertyNames_ShouldUpdateSingleProperty()
    {
        // Arrange
        var obj = new TestNostifyObject();
        var payload = new { name = "Test Name", age = 25 };

        // Act
        obj.UpdateProperty<TestNostifyObject>("name", "name", payload);

        // Assert
        Assert.Equal("Test Name", obj.name);
        Assert.Equal(0, obj.age); // Should remain default
    }

    [Fact]
    public void UpdateProperty_WithDifferentSourceAndTargetProperties_ShouldMapCorrectly()
    {
        // Arrange
        var obj = new TestProjection();
        var payload = new { userName = "John Doe", userAge = 30 };

        // Act
        obj.UpdateProperty<TestProjection>("fullName", "userName", payload);

        // Assert
        Assert.Equal("John Doe", obj.fullName);
        Assert.Equal(0, obj.userAge); // Should remain default
    }

    [Fact]
    public void UpdateProperty_WithJObjectPayload_ShouldUpdateProperty()
    {
        // Arrange
        var obj = new TestNostifyObject();
        var jPayload = JObject.FromObject(new { name = "Test Name", age = 25 });

        // Act
        obj.UpdateProperty<TestNostifyObject>("name", "name", jPayload);

        // Assert
        Assert.Equal("Test Name", obj.name);
    }

    [Fact]
    public void UpdateProperty_WithPrecomputedPropertyList_ShouldUpdateProperty()
    {
        // Arrange
        var obj = new TestNostifyObject();
        var payload = new { name = "Test Name" };
        var props = typeof(TestNostifyObject).GetProperties(BindingFlags.Public | BindingFlags.Instance).ToList();

        // Act
        obj.UpdateProperty<TestNostifyObject>("name", "name", payload, props);

        // Assert
        Assert.Equal("Test Name", obj.name);
    }

    [Fact]
    public void UpdateProperty_WithNonExistentTargetProperty_ShouldNotThrowError()
    {
        // Arrange
        var obj = new TestNostifyObject();
        var payload = new { name = "Test Name" };

        // Act & Assert - Should not throw
        obj.UpdateProperty<TestNostifyObject>("nonExistentProperty", "name", payload);
        
        // Original property should remain unchanged
        Assert.Null(obj.name);
    }

    [Fact]
    public void UpdateProperty_WithNonExistentSourceProperty_ShouldNotThrowError()
    {
        // Arrange
        var obj = new TestNostifyObject();
        var payload = new { name = "Test Name" };

        // Act & Assert - Should not throw
        obj.UpdateProperty<TestNostifyObject>("name", "nonExistentProperty", payload);
        
        // Target property should remain unchanged
        Assert.Null(obj.name);
    }

    [Fact]
    public void UpdateProperties_WithNullPayload_ShouldThrowException()
    {
        // Arrange
        var obj = new TestNostifyObject();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            obj.UpdateProperties<TestNostifyObject>(null!));
    }

    [Fact]
    public void UpdateProperties_WithEmptyPayload_ShouldNotUpdateAnyProperties()
    {
        // Arrange
        var obj = new TestNostifyObject
        {
            name = "Original Name",
            age = 25
        };
        var payload = new { };

        // Act
        obj.UpdateProperties<TestNostifyObject>(payload);

        // Assert - Properties should remain unchanged
        Assert.Equal("Original Name", obj.name);
        Assert.Equal(25, obj.age);
    }

    [Fact]
    public void Interfaces_ShouldBeImplemented()
    {
        // Arrange
        var obj = new TestNostifyObject();

        // Assert
        Assert.IsAssignableFrom<ITenantFilterable>(obj);
        Assert.IsAssignableFrom<IUniquelyIdentifiable>(obj);
        Assert.IsAssignableFrom<IApplyable>(obj);
    }

    [Theory]
    [InlineData("string value")]
    [InlineData(42)]
    [InlineData(true)]
    [InlineData(3.14)]
    public void UpdateProperties_WithVariousDataTypes_ShouldHandleTypeConversions(object value)
    {
        // Arrange
        var obj = new TestNostifyObject();
        var payload = JObject.FromObject(new Dictionary<string, object> { { "name", value } });

        // Act
        obj.UpdateProperty<TestNostifyObject>("name", "name", payload);

        // Assert
        // The actual value will depend on the NostifyExtensions.GetValue implementation
        // Here we just verify no exception is thrown and the method completes
        Assert.NotNull(obj);
    }

    [Fact]
    public void TTL_DefaultValue_ShouldBeNegativeOne()
    {
        // Arrange & Act
        var obj = new TestNostifyObject();

        // Assert
        Assert.Equal(-1, obj.ttl);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3600)]
    [InlineData(86400)]
    [InlineData(int.MaxValue)]
    public void TTL_ShouldAcceptPositiveValues(int ttlValue)
    {
        // Arrange
        var obj = new TestNostifyObject();

        // Act
        obj.ttl = ttlValue;

        // Assert
        Assert.Equal(ttlValue, obj.ttl);
    }

    [Fact]
    public void Apply_ShouldBeAbstractMethod()
    {
        // Arrange
        var obj = new TestNostifyObject();
        var command = new NostifyCommand("Test");
        var evt = new Event(command, new { id = Guid.NewGuid() });

        // Act & Assert
        Assert.Throws<NotImplementedException>(() => obj.Apply(evt));
    }
}

/// <summary>
/// Tests for the new UpdateProperties overload that uses PropertyCheck objects
/// </summary>
public class NostifyObjectPropertyCheckTests
{
    // Test projection with multiple properties that could match the same event type
    public class ComplexProjection : NostifyObject, IProjection
    {
        public static string containerName => "ComplexProjection";
        public bool initialized { get; set; } = false;

        // Multiple properties that might be updated from User events
        public Guid? primaryUserId { get; set; }
        public string? primaryUserName { get; set; }
        public string? primaryUserEmail { get; set; }
        
        public Guid? secondaryUserId { get; set; }
        public string? secondaryUserName { get; set; }
        public string? secondaryUserEmail { get; set; }
        
        public Guid? managerUserId { get; set; }
        public string? managerUserName { get; set; }
        public string? managerUserEmail { get; set; }

        // Additional properties for testing
        public string? department { get; set; }
        public DateTime? lastUpdated { get; set; }
        public bool isActive { get; set; }

        public override void Apply(IEvent eventToApply)
        {
            throw new NotImplementedException("Apply method not implemented for test");
        }

        // Helper method to call the protected UpdateProperties method
        public void CallUpdateProperties(Guid eventAggregateRootId, object payload, List<PropertyCheck> propertyCheckValues)
        {
            UpdateProperties<ComplexProjection>(eventAggregateRootId, payload, propertyCheckValues);
        }
    }

    [Fact]
    public void UpdateProperties_WithPropertyCheck_ShouldUpdateCorrectPropertyBasedOnIdMatch()
    {
        // Arrange
        var projection = new ComplexProjection
        {
            primaryUserId = Guid.NewGuid(),
            secondaryUserId = Guid.NewGuid(),
            managerUserId = Guid.NewGuid()
        };

        var userUpdatePayload = new
        {
            name = "John Doe",
            email = "john.doe@example.com"
        };

        var propertyChecks = new List<PropertyCheck>
        {
            new PropertyCheck("primaryUserId", "name", "primaryUserName"),
            new PropertyCheck("primaryUserId", "email", "primaryUserEmail"),
            new PropertyCheck("secondaryUserId", "name", "secondaryUserName"),
            new PropertyCheck("secondaryUserId", "email", "secondaryUserEmail"),
            new PropertyCheck("managerUserId", "name", "managerUserName"),
            new PropertyCheck("managerUserId", "email", "managerUserEmail")
        };

        // Act - Update primary user (matching primaryUserId)
        projection.CallUpdateProperties(projection.primaryUserId!.Value, userUpdatePayload, propertyChecks);

        // Assert
        Assert.Equal("John Doe", projection.primaryUserName);
        Assert.Equal("john.doe@example.com", projection.primaryUserEmail);
        
        // Other user properties should remain null
        Assert.Null(projection.secondaryUserName);
        Assert.Null(projection.secondaryUserEmail);
        Assert.Null(projection.managerUserName);
        Assert.Null(projection.managerUserEmail);
    }

    [Fact]
    public void UpdateProperties_WithPropertyCheck_ShouldUpdateSecondaryUserWhenSecondaryIdMatches()
    {
        // Arrange
        var projection = new ComplexProjection
        {
            primaryUserId = Guid.NewGuid(),
            secondaryUserId = Guid.NewGuid(),
            managerUserId = Guid.NewGuid()
        };

        var userUpdatePayload = new
        {
            name = "Jane Smith",
            email = "jane.smith@example.com"
        };

        var propertyChecks = new List<PropertyCheck>
        {
            new PropertyCheck("primaryUserId", "name", "primaryUserName"),
            new PropertyCheck("primaryUserId", "email", "primaryUserEmail"),
            new PropertyCheck("secondaryUserId", "name", "secondaryUserName"),
            new PropertyCheck("secondaryUserId", "email", "secondaryUserEmail"),
            new PropertyCheck("managerUserId", "name", "managerUserName"),
            new PropertyCheck("managerUserId", "email", "managerUserEmail")
        };

        // Act - Update secondary user (matching secondaryUserId)
        projection.CallUpdateProperties(projection.secondaryUserId!.Value, userUpdatePayload, propertyChecks);

        // Assert
        Assert.Equal("Jane Smith", projection.secondaryUserName);
        Assert.Equal("jane.smith@example.com", projection.secondaryUserEmail);
        
        // Other user properties should remain null
        Assert.Null(projection.primaryUserName);
        Assert.Null(projection.primaryUserEmail);
        Assert.Null(projection.managerUserName);
        Assert.Null(projection.managerUserEmail);
    }

    [Fact]
    public void UpdateProperties_WithPropertyCheck_ShouldUpdateManagerUserWhenManagerIdMatches()
    {
        // Arrange
        var projection = new ComplexProjection
        {
            primaryUserId = Guid.NewGuid(),
            secondaryUserId = Guid.NewGuid(),
            managerUserId = Guid.NewGuid()
        };

        var userUpdatePayload = new
        {
            name = "Bob Manager",
            email = "bob.manager@example.com"
        };

        var propertyChecks = new List<PropertyCheck>
        {
            new PropertyCheck("primaryUserId", "name", "primaryUserName"),
            new PropertyCheck("primaryUserId", "email", "primaryUserEmail"),
            new PropertyCheck("secondaryUserId", "name", "secondaryUserName"),
            new PropertyCheck("secondaryUserId", "email", "secondaryUserEmail"),
            new PropertyCheck("managerUserId", "name", "managerUserName"),
            new PropertyCheck("managerUserId", "email", "managerUserEmail")
        };

        // Act - Update manager user (matching managerUserId)
        projection.CallUpdateProperties(projection.managerUserId!.Value, userUpdatePayload, propertyChecks);

        // Assert
        Assert.Equal("Bob Manager", projection.managerUserName);
        Assert.Equal("bob.manager@example.com", projection.managerUserEmail);
        
        // Other user properties should remain null
        Assert.Null(projection.primaryUserName);
        Assert.Null(projection.primaryUserEmail);
        Assert.Null(projection.secondaryUserName);
        Assert.Null(projection.secondaryUserEmail);
    }

    [Fact]
    public void UpdateProperties_WithPropertyCheck_ShouldNotUpdateWhenNoIdMatches()
    {
        // Arrange
        var projection = new ComplexProjection
        {
            primaryUserId = Guid.NewGuid(),
            secondaryUserId = Guid.NewGuid(),
            managerUserId = Guid.NewGuid(),
            primaryUserName = "Existing Name",
            secondaryUserName = "Another Existing Name"
        };

        var userUpdatePayload = new
        {
            name = "Should Not Update",
            email = "should.not.update@example.com"
        };

        var propertyChecks = new List<PropertyCheck>
        {
            new PropertyCheck("primaryUserId", "name", "primaryUserName"),
            new PropertyCheck("primaryUserId", "email", "primaryUserEmail"),
            new PropertyCheck("secondaryUserId", "name", "secondaryUserName"),
            new PropertyCheck("secondaryUserId", "email", "secondaryUserEmail")
        };

        // Act - Use a random Guid that doesn't match any of the user IDs
        var randomGuid = Guid.NewGuid();
        projection.CallUpdateProperties(randomGuid, userUpdatePayload, propertyChecks);

        // Assert - All properties should remain unchanged
        Assert.Equal("Existing Name", projection.primaryUserName);
        Assert.Equal("Another Existing Name", projection.secondaryUserName);
        Assert.Null(projection.primaryUserEmail);
        Assert.Null(projection.secondaryUserEmail);
    }

    [Fact]
    public void UpdateProperties_WithPropertyCheck_ShouldHandlePartialPayload()
    {
        // Arrange
        var projection = new ComplexProjection
        {
            primaryUserId = Guid.NewGuid(),
            secondaryUserId = Guid.NewGuid()
        };

        var partialUserUpdatePayload = new
        {
            name = "John Partial"
            // email is missing from payload
        };

        var propertyChecks = new List<PropertyCheck>
        {
            new PropertyCheck("primaryUserId", "name", "primaryUserName"),
            new PropertyCheck("primaryUserId", "email", "primaryUserEmail")
        };

        // Act
        projection.CallUpdateProperties(projection.primaryUserId!.Value, partialUserUpdatePayload, propertyChecks);

        // Assert
        Assert.Equal("John Partial", projection.primaryUserName);
        Assert.Null(projection.primaryUserEmail); // Should remain null since email not in payload
    }

    [Fact]
    public void UpdateProperties_WithPropertyCheck_ShouldHandleMultipleMatchingProperties()
    {
        // Arrange
        var projection = new ComplexProjection
        {
            primaryUserId = Guid.NewGuid()
        };

        var fullUserUpdatePayload = new
        {
            name = "Complete User",
            email = "complete.user@example.com"
        };

        var propertyChecks = new List<PropertyCheck>
        {
            new PropertyCheck("primaryUserId", "name", "primaryUserName"),
            new PropertyCheck("primaryUserId", "email", "primaryUserEmail")
        };

        // Act
        projection.CallUpdateProperties(projection.primaryUserId!.Value, fullUserUpdatePayload, propertyChecks);

        // Assert - Both properties should be updated
        Assert.Equal("Complete User", projection.primaryUserName);
        Assert.Equal("complete.user@example.com", projection.primaryUserEmail);
    }

    [Fact]
    public void UpdateProperties_WithPropertyCheck_ShouldIgnorePropertyChecksWithNonExistentIdProperty()
    {
        // Arrange
        var projection = new ComplexProjection
        {
            primaryUserId = Guid.NewGuid()
        };

        var userUpdatePayload = new
        {
            name = "Test User",
            email = "test@example.com"
        };

        var propertyChecks = new List<PropertyCheck>
        {
            new PropertyCheck("nonExistentIdProperty", "name", "primaryUserName"), // Invalid ID property
            new PropertyCheck("primaryUserId", "email", "primaryUserEmail") // Valid property check
        };

        // Act
        projection.CallUpdateProperties(projection.primaryUserId!.Value, userUpdatePayload, propertyChecks);

        // Assert - Only the valid property check should work
        Assert.Null(projection.primaryUserName); // Should not be updated due to invalid ID property
        Assert.Equal("test@example.com", projection.primaryUserEmail); // Should be updated
    }

    [Fact]
    public void UpdateProperties_WithPropertyCheck_ShouldHandleNullIdValues()
    {
        // Arrange
        var projection = new ComplexProjection
        {
            primaryUserId = null, // Null ID
            secondaryUserId = Guid.NewGuid()
        };

        var userUpdatePayload = new
        {
            name = "Test User",
            email = "test@example.com"
        };

        var propertyChecks = new List<PropertyCheck>
        {
            new PropertyCheck("primaryUserId", "name", "primaryUserName"),
            new PropertyCheck("secondaryUserId", "name", "secondaryUserName")
        };

        // Act - Try to match against the null primaryUserId
        projection.CallUpdateProperties(Guid.Empty, userUpdatePayload, propertyChecks);

        // Assert - No properties should be updated since Guid.Empty doesn't match null
        Assert.Null(projection.primaryUserName);
        Assert.Null(projection.secondaryUserName);
    }

    [Fact]
    public void UpdateProperties_WithPropertyCheck_ShouldHandleEmptyPropertyCheckList()
    {
        // Arrange
        var projection = new ComplexProjection
        {
            primaryUserId = Guid.NewGuid(),
            primaryUserName = "Existing Name"
        };

        var userUpdatePayload = new
        {
            name = "Should Not Update"
        };

        var emptyPropertyChecks = new List<PropertyCheck>();

        // Act
        projection.CallUpdateProperties(projection.primaryUserId!.Value, userUpdatePayload, emptyPropertyChecks);

        // Assert - Properties should remain unchanged
        Assert.Equal("Existing Name", projection.primaryUserName);
    }

    [Fact]
    public void UpdateProperties_WithPropertyCheck_ShouldHandleDifferentDataTypes()
    {
        // Arrange
        var projection = new ComplexProjection
        {
            primaryUserId = Guid.NewGuid()
        };

        var mixedPayload = new
        {
            department = "Engineering",
            lastUpdated = DateTime.Now,
            isActive = true
        };

        var propertyChecks = new List<PropertyCheck>
        {
            new PropertyCheck("primaryUserId", "department", "department"),
            new PropertyCheck("primaryUserId", "lastUpdated", "lastUpdated"),
            new PropertyCheck("primaryUserId", "isActive", "isActive")
        };

        // Act
        projection.CallUpdateProperties(projection.primaryUserId!.Value, mixedPayload, propertyChecks);

        // Assert
        Assert.Equal("Engineering", projection.department);
        Assert.True(projection.lastUpdated.HasValue);
        Assert.True(projection.isActive);
    }

    [Fact]
    public void PropertyCheck_Constructor_ShouldSetPropertiesCorrectly()
    {
        // Arrange & Act
        var propertyCheck = new PropertyCheck("idPropertyName", "sourceProperty", "targetProperty");

        // Assert
        Assert.Equal("idPropertyName", propertyCheck.projectionIdPropertyName);
        Assert.Equal("sourceProperty", propertyCheck.eventPropertyName);
        Assert.Equal("targetProperty", propertyCheck.projectionPropertyName);
    }

    [Fact]
    public void UpdateProperties_WithPropertyCheck_ShouldHandleComplexScenarioWithMultipleUpdates()
    {
        // Arrange - Complex scenario with multiple different property updates
        var projection = new ComplexProjection
        {
            primaryUserId = Guid.NewGuid(),
            secondaryUserId = Guid.NewGuid(),
            managerUserId = Guid.NewGuid()
        };

        var primaryUserPayload = new { name = "Primary User", email = "primary@test.com" };
        var secondaryUserPayload = new { name = "Secondary User", email = "secondary@test.com" };
        var managerUserPayload = new { name = "Manager User", email = "manager@test.com" };

        var propertyChecks = new List<PropertyCheck>
        {
            new PropertyCheck("primaryUserId", "name", "primaryUserName"),
            new PropertyCheck("primaryUserId", "email", "primaryUserEmail"),
            new PropertyCheck("secondaryUserId", "name", "secondaryUserName"),
            new PropertyCheck("secondaryUserId", "email", "secondaryUserEmail"),
            new PropertyCheck("managerUserId", "name", "managerUserName"),
            new PropertyCheck("managerUserId", "email", "managerUserEmail")
        };

        // Act - Apply multiple updates
        projection.CallUpdateProperties(projection.primaryUserId!.Value, primaryUserPayload, propertyChecks);
        projection.CallUpdateProperties(projection.secondaryUserId!.Value, secondaryUserPayload, propertyChecks);
        projection.CallUpdateProperties(projection.managerUserId!.Value, managerUserPayload, propertyChecks);

        // Assert - All properties should be correctly updated
        Assert.Equal("Primary User", projection.primaryUserName);
        Assert.Equal("primary@test.com", projection.primaryUserEmail);
        Assert.Equal("Secondary User", projection.secondaryUserName);
        Assert.Equal("secondary@test.com", projection.secondaryUserEmail);
        Assert.Equal("Manager User", projection.managerUserName);
        Assert.Equal("manager@test.com", projection.managerUserEmail);
    }
}
