# Add project specific ProGuard rules here.
-keepattributes Signature
-keepattributes *Annotation*
-keep class mx.cfe.grijalva.pih.data.model.** { *; }
-keep class com.microsoft.signalr.** { *; }
-dontwarn org.slf4j.**
