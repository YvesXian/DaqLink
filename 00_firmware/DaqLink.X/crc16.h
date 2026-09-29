/* 
 * File:   crc16.h
 * Author: user
 *
 * Created on 2026年9月29日, 下午 4:14
 */

#ifndef CRC16_H
#define	CRC16_H

#include <stdint.h>

#ifdef	__cplusplus
extern "C" {
#endif
    
#define CRC16_INIT 0xFFFF
    uint16_t crc16_update(uint16_t crc, uint8_t b);
    uint16_t crc16_calc(const uint8_t *buf, uint16_t len);

#ifdef	__cplusplus
}
#endif

#endif	/* CRC16_H */

