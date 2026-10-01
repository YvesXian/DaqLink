/* 
 * File:   wave.h
 * Author: user
 *
 * Created on 2026年9月30日, 下午 11:21
 */

#ifndef WAVE_H
#define	WAVE_H

#include <stdint.h>

#define WAVE_CH_A       0
#define WAVE_CH_B       1

#define WAVE_DC         0
#define WAVE_SINE       1
#define WAVE_TRI        2
#define WAVE_SQUARE     3

#define WAVE_CODE_MIN   100     /* 避開 MCP4922 軌限區域 */
#define WAVE_CODE_MAX   3995

void     wave_init(uint16_t rate_hz);
int      wave_params_ok(uint8_t ch, uint8_t type, uint16_t freq_x10,
                        uint16_t amp, uint16_t offset, uint16_t rate_hz);
void     wave_set(uint8_t ch, uint8_t type, uint16_t freq_x10,
                  uint16_t amp, uint16_t offset, uint16_t rate_hz);
int      wave_rate_ok(uint16_t rate_hz);
void     wave_set_rate(uint16_t rate_hz);
void     wave_reset_phase(void);
uint16_t wave_next(uint8_t ch);

#ifdef	__cplusplus
extern "C" {
#endif




#ifdef	__cplusplus
}
#endif

#endif	/* WAVE_H */

