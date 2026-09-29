#include "crc16.h"

#define CRC16_POLY 0x1021

uint16_t crc16_update(uint16_t crc, uint8_t b)
{
    uint8_t i;
    
    crc ^= (uint16_t) b << 8;
    for(i = 0; i < 8; i++)
    {
        if(crc & 0x8000)
            crc = (uint16_t)((crc << 1) ^ CRC16_POLY);
        else
            crc = (uint16_t)(crc << 1);
    }
    
    return crc;
}

uint16_t crc16_calc(const uint8_t *buf, uint16_t len)
{
    uint16_t crc = CRC16_INIT;

    while (len--)
        crc = crc16_update(crc, *buf++);
    return crc;
}