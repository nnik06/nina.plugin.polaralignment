using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using NINA.Plugins.PolarAlignment.Qualification;
using NUnit.Framework;

namespace NINA.Plugins.PolarAlignment.Test;
[TestFixture]
public class SupervisorGainMeasurementExportTest {
    [TestCase(false,false,false,true)] [TestCase(true,true,false,true)]
    [TestCase(true,false,true,true)] [TestCase(true,false,false,false)]
    public void RefusesAnyActuatorRouteOrNonTruePoleMode(bool only,bool route,bool automated,bool refraction) =>
        Assert.Throws<InvalidOperationException>(()=>SupervisorGainMeasurementExport.RequireMeasurementOnly(only,route,automated,refraction));
    [Test]
    public void RouteRejectsMissingOrChangedOpticalConfiguration() {
        var id=new string('b',64);
        Assert.Throws<InvalidOperationException>(()=>SupervisorSkyRoute.RequireOpticalConfiguration(id,_=>null));
        Assert.Throws<InvalidOperationException>(()=>SupervisorSkyRoute.RequireOpticalConfiguration(id,_=>new string('c',64)));
        Assert.DoesNotThrow(()=>SupervisorSkyRoute.RequireOpticalConfiguration(id,_=>id));
    }
    [Test]
    public void OriginalThreePointExportHasNoAdoptedBoundAndCannotOverwrite() {
        var folder=Path.Combine(TestContext.CurrentContext.WorkDirectory,Guid.NewGuid().ToString("D"));Directory.CreateDirectory(folder);
        try {
            var path=Path.Combine(folder,"REQUEST.json");var now=DateTime.UtcNow;
            File.WriteAllText(path,JsonSerializer.Serialize(new {scope="ATTENDED_SKY_GAIN_MEASUREMENT",owner_sky_enabled=true,
                measurementSessionId=Guid.NewGuid().ToString("D"),host_id=Environment.MachineName,
                readyAfterUtc=now.AddSeconds(-5).ToString("O"),expiresAtUtc=now.AddSeconds(60).ToString("O"),hemisphere="NORTH",
                optical_configuration_id=new string('b',64),binding_pins=new Dictionary<string,string>{{"fixture",new string('a',64)}},outputFile="0.json"}));
            var exporter=new SupervisorGainMeasurementExport(path,true,false,false,true,new string('b',64));var times=new[]{now.AddSeconds(-3),now.AddSeconds(-2),now.AddSeconds(-1)};
            exporter.Publish(12,-6,"NORTH",times,true,new string('b',64));
            using var output=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder,"0.json")));
            Assert.That(output.RootElement.GetProperty("absolute_accuracy_qualified").GetBoolean(),Is.False);
            Assert.That(output.RootElement.GetProperty("measuredAtUtc").GetString(),Is.EqualTo(times[2].ToString("O")));
            Assert.That(output.RootElement.TryGetProperty("sky_error_intervals_arcmin",out _),Is.False);
            Assert.Throws<IOException>(()=>exporter.Publish(12,-6,"NORTH",times,true,new string('b',64)));
            Assert.That(Directory.GetFiles(folder,"*.tmp"),Is.Empty);
            Assert.Throws<InvalidDataException>(()=>exporter.Publish(12,-6,"NORTH",times,true,new string('c',64)));
            Assert.Throws<InvalidOperationException>(()=>new SupervisorGainMeasurementExport(path,false,false,false,true,new string('b',64)));
            Assert.Throws<InvalidDataException>(()=>new SupervisorGainMeasurementExport(path,true,false,false,true,new string('c',64)));
            Assert.Throws<InvalidDataException>(()=>exporter.Publish(12,-6,"SOUTH",times,true,new string('b',64)));
            Assert.Throws<InvalidDataException>(()=>exporter.Publish(12,-6,"NORTH",times,false,new string('b',64)));
        } finally {Directory.Delete(folder,true);}
    }
}
