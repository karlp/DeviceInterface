#!/bin/bash
BASE_URI="http://10.8.0.1/fpga_bitstreams/"
VERSIONS_TO_FETCH=( A10 A12 A14 A15 A16 A17 )
BITSTREAM_FILE_PREFIX="SmartScope_"
BITSTREAM_FILE_SUFFIX=".bin"

for i in "${VERSIONS_TO_FETCH[@]}"; do
  BITSTREAM_FILENAME="${BITSTREAM_FILE_PREFIX}${i}${BITSTREAM_FILE_SUFFIX}"
  BITSTREAM_URI="${BASE_URI}${i}/${BITSTREAM_FILENAME}"
  echo Fetching ${BITSTREAM_URI} TO ${BITSTREAM_FILENAME}
  curl ${BITSTREAM_URI} -# -o ${BITSTREAM_FILENAME}
done
