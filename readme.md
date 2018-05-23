# MSHLS FHIR Server DTSU3

This is an experimental FHIR Server built to the DTSU3 specification of HL7 FHIR. It is build using only PaaS components of Microsoft Azure.
It supports all resource types and REST CRUD operations. You can also enable SMART Security Profile. Most common resource queries are supported and more can be configured
in the FHIRParameterMapping.txt FHIR without recompiling. See Instructions in file.

Simply click the button below to deploy a full functioning version
of the FHIR Server.  Note: OAUTH is not enabled by default.

[![Deploy to Azure](http://azuredeploy.net/deploybutton.png)](https://azuredeploy.net/)

You can check deployment success by accessing the conformance statement of the newly deployed FHIRServer:
```
https://<fhirservername>.azurewebsites.net/metadata?_format=json
```


## Authors

* **Steven Ordahl** - Microsoft HLS Apps and Infrastructure Cloud Architect
