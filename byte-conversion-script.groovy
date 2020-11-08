bout = new ByteArrayOutputStream()
out = new DataOutputStream(bout)

//out.writeShort(-30000)
//out.writeInt(-2_000_000_100)
out.writeLong(-9_199_999_999_999_999_999L)

print bout.toByteArray().collect{ it & 0xff }
